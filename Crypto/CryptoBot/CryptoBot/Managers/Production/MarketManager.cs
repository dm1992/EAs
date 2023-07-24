using Bybit.Net;
using Bybit.Net.Clients;
using Bybit.Net.Enums;
using Bybit.Net.Objects;
using Bybit.Net.Objects.Models.Spot.v3;
using Bybit.Net.Objects.Models.V5;
using Bybit.Net.Objects.Options;
using CryptoBot.EventArgs;
using CryptoBot.Interfaces.Managers;
using CryptoBot.Models;
using CryptoExchange.Net.Objects;
using CryptoExchange.Net.Sockets;
using NLog;
using NLog.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CryptoBot.Managers.Production
{
    public class MarketManager : IMarketManager
    {
        private const int SUBSCRIPTION_STATE_DELAY = 5000;
        private const int MARKETSIGNAL_MONITOR_DELAY = 30000;

        private readonly Config _config;
        private readonly ITradingManager _tradingManager;
        private readonly SemaphoreSlim _tickerSemaphore;
        private readonly SemaphoreSlim _marketTradeSemaphore;
        private readonly SemaphoreSlim _orderbookSnapshotSemaphore;
        private readonly SemaphoreSlim _orderbookUpdateSemaphore;
        private readonly BybitSocketClient _webSocket;

        private decimal _totalROI = 0;
        private decimal _maxROI = decimal.MinValue;
        private decimal _minROI = decimal.MaxValue;

        private NLog.ILogger _logger;
        private NLog.ILogger _verboseLogger;
        private bool _isInitialized;
        private Dictionary<string, List<BybitTrade>> _marketTradeBuffer;
        private Dictionary<string, BybitOrderbook> _orderbookBuffer;
        private Dictionary<string, AutoResetEvent> _marketEntityPendingStateBuffer;
        private Dictionary<string, AutoResetEvent> _marketInformationPendingStateBuffer;
        private Dictionary<string, BybitSpotTickerUpdate> _marketTickerBuffer;
        private Dictionary<string, MarketSignal> _marketSignalBuffer;
        private Dictionary<string, List<MarketEntity>> _marketEntityCollection;
        private Dictionary<string, List<MarketInformation>> _marketInformationCollection;

        public MarketManager(LogFactory logFactory, ITradingManager tradingManager, Config config)
        {
            _logger = logFactory.GetCurrentClassLogger();
            _verboseLogger = logFactory.GetLogger("verbose");
            _tradingManager = tradingManager;
            _config = config;

            _tickerSemaphore = new SemaphoreSlim(1, 1);
            _marketTradeSemaphore = new SemaphoreSlim(1, 1);
            _orderbookSnapshotSemaphore = new SemaphoreSlim(1, 1);
            _orderbookUpdateSemaphore = new SemaphoreSlim(1, 1);

            _webSocket = new BybitSocketClient(optionsDelegate =>
            {
                optionsDelegate.AutoReconnect = true;
                optionsDelegate.Environment = BybitEnvironment.Live;
            });

            _isInitialized = false;
            _orderbookBuffer = new Dictionary<string, BybitOrderbook>();
            _marketTradeBuffer = new Dictionary<string, List<BybitTrade>>();
            _marketEntityPendingStateBuffer = new Dictionary<string, AutoResetEvent>();
            _marketInformationPendingStateBuffer = new Dictionary<string, AutoResetEvent>();
            _marketTickerBuffer = new Dictionary<string, BybitSpotTickerUpdate>();
            _marketSignalBuffer = new Dictionary<string, MarketSignal>();
            _marketEntityCollection = new Dictionary<string, List<MarketEntity>>();
            _marketInformationCollection = new Dictionary<string, List<MarketInformation>>();
        }

        public bool Initialize()
        {
            try
            {
                if (_isInitialized) return true;

                // run thread workers
                Task.Run(() => { HandleMarketEntitesInThread(); });
                Task.Run(() => { HandleMarketInformationsInThread(); });
                Task.Run(() => { MonitorMarketSignalsInThread(); });
                Task.Run(() => { MonitorWebSocketSubscriptionStates(); });

                _logger.Info("Initialized.");

                _isInitialized = true;
                return true;
            }
            catch (Exception e)
            {
                _logger.Error($"Initialization failed. {e}");
                return false;
            }
        }

        public void InvokeWebSocketSubscription()
        {
            try
            {
                SubscribeToTickerUpdatesAsync();

                SubscribeToTradeUpdatesAsync();

                SubscribeToOrderbookUpdatesAsync();
            }
            catch (Exception e)
            {
                _logger.Error($"Failed InvokeWebSocketEventSubscription. {e}");
            }
        }

        public async void RefreshWebSocketSubscription()
        {
            _logger.Info("Refreshing web socket event subscription.");

            await _webSocket.V5SpotApi.ReconnectAsync();
        }

        public async void CloseWebSocketSubscription()
        {
            _logger.Info("Closing web socket event subscription.");

            await _webSocket.V5SpotApi.UnsubscribeAllAsync();
        }

        private async void SubscribeToTickerUpdatesAsync()
        {
            _logger.Info("Subscribing to ticker updates.");

            CallResult<UpdateSubscription> response = await _webSocket.V5SpotApi.SubscribeToTickerUpdatesAsync(_config.Symbols, HandleTicker);

            if (!response.GetResultOrError(out UpdateSubscription updateSubscription, out Error error))
            {
                throw new Exception($"Failed to subscribe to ticker updates. Error: ({error?.Code}) {error?.Message}.");
            }

            updateSubscription.ConnectionRestored += WebSocketEventSubscription_TickerConnectionRestored;
            updateSubscription.ConnectionLost += WebSocketEventSubscription_TickerConnectionLost;
            updateSubscription.ConnectionClosed += WebSocketEventSubscription_TickerConnectionClosed;
        }

        private async void SubscribeToTradeUpdatesAsync()
        {
            _logger.Info("Subscribing to trade updates.");

            CallResult<UpdateSubscription> response = await _webSocket.V5SpotApi.SubscribeToTradeUpdatesAsync(_config.Symbols, HandleMarketTrades);

            if (!response.GetResultOrError(out UpdateSubscription updateSubscription, out Error error))
            {
                throw new Exception($"Failed to subscribe to trade updates. Error: ({error?.Code}) {error?.Message}.");
            }

            updateSubscription.ConnectionRestored += WebSocketEventSubscription_MarketTradeUpdatesConnectionRestored;
            updateSubscription.ConnectionLost += WebSocketEventSubscription_MarketTradeUpdatesConnectionLost;
            updateSubscription.ConnectionClosed += WebSocketEventSubscription_MarketTradeUpdatesConnectionClosed;
        }

        private async void SubscribeToOrderbookUpdatesAsync()
        {
            _logger.Info("Subscribing to orderbook updates.");

            CallResult<UpdateSubscription> response = await _webSocket.V5SpotApi.SubscribeToOrderbookUpdatesAsync(_config.Symbols, 50, HandleOrderbookSnapshot, HandleOrderbookUpdate);

            if (!response.GetResultOrError(out UpdateSubscription updateSubscription, out Error error))
            {
                throw new Exception($"Failed to subscribe to orderbook updates. Error: ({error?.Code}) {error?.Message}.");
            }

            updateSubscription.ConnectionRestored += WebSocketEventSubscription_OrderbookUpdatesConnectionRestored;
            updateSubscription.ConnectionLost += WebSocketEventSubscription_OrderbookUpdatesConnectionLost;
            updateSubscription.ConnectionClosed += WebSocketEventSubscription_OrderbookUpdatesConnectionClosed;
        }

        private bool IsMarketEntityWindowReady(string symbol)
        {
            return GetOrderedMarketEntities(symbol).Count() >= _config.MarketEntityWindowSize;
        }

        private bool IsMarketInformationWindowReady(string symbol)
        {
            return GetOrderedMarketInformations(symbol).Count() >= _config.MarketInformationWindowSize;
        }

        private bool IsNewMarketEntityAvailable(string symbol)
        {
            lock (_marketEntityPendingStateBuffer)
            {
                if (!_marketEntityPendingStateBuffer.TryGetValue(symbol, out AutoResetEvent marketEntityPendingState))
                    return false;

                return marketEntityPendingState.WaitOne(0);
            }
        }

        private bool IsNewMarketInformationAvailable(string symbol)
        {
            lock (_marketInformationPendingStateBuffer)
            {
                if (!_marketInformationPendingStateBuffer.TryGetValue(symbol, out AutoResetEvent marketInformationPendingState))
                    return false;

                return marketInformationPendingState.WaitOne(0);
            }
        }

        private void SetMarketEntityPendingState(string symbol)
        {
            lock (_marketEntityPendingStateBuffer)
            {
                if (!_marketEntityPendingStateBuffer.TryGetValue(symbol, out _))
                {
                    AutoResetEvent marketEntityPendingState = new AutoResetEvent(true);

                    _marketEntityPendingStateBuffer.Add(symbol, marketEntityPendingState);
                }
                else
                {
                    _marketEntityPendingStateBuffer[symbol].Set();
                }
            }
        }

        private void SetMarketInformationPendingState(string symbol)
        {
            lock (_marketInformationPendingStateBuffer)
            {
                if (!_marketInformationPendingStateBuffer.TryGetValue(symbol, out _))
                {
                    AutoResetEvent marketInformationPendingState = new AutoResetEvent(true);

                    _marketInformationPendingStateBuffer.Add(symbol, marketInformationPendingState);
                }
                else
                {
                    _marketInformationPendingStateBuffer[symbol].Set();
                }
            }
        }

        private async Task EvaluateMarketInformations(string symbol)
        {
            try
            {
                MarketDirection marketDirection = GetLatestMarketInformationMarketDirection(symbol);

                if (marketDirection == MarketDirection.Unknown)
                    return;

                MarketSignal latestMarketSignal = GetLatestMarketSignal(symbol);

                if (latestMarketSignal != null)
                {
                    if (latestMarketSignal.MarketDirection == marketDirection)
                        return;

                    await RemoveMarketSignal(symbol);
                }

                MarketSignal marketSignal = await CreateMarketSignal(symbol, marketDirection);

                if (marketSignal != null)
                {
                    _verboseLogger.Debug("CREATED >>> " + marketSignal.DumpCreated());

                    UpdateMarketSignal(marketSignal);
                }
            }
            finally
            {
                RemoveHistoricMarketInformation(symbol);
            }
        }

        private MarketDirection GetLatestMarketInformationMarketDirection(string symbol)
        {
            if (!IsMarketInformationWindowReady(symbol))
                return MarketDirection.Unknown;

            return GetLatestMarketInformation(symbol)?.GetMarketDirection() ?? MarketDirection.Unknown;
        }

        private async Task<bool> CreateMarketSignalOrder(MarketSignal marketSignal)
        {
            if (marketSignal == null)
                return false;

            if (marketSignal.MarketDirection == MarketDirection.Unknown)
                return false;

            BybitSpotOrderV3 order = new BybitSpotOrderV3();
            order.Symbol = marketSignal.Symbol;
            order.Quantity = marketSignal.MarketDirection == MarketDirection.Uptrend ? _config.BuyQuantity : _config.SellQuantity;
            order.Side = marketSignal.MarketDirection == MarketDirection.Uptrend ? OrderSide.Buy : OrderSide.Sell;

            if (!await _tradingManager.PlaceOrder(order))
            {
                _logger.Error($"Failed to create market signal order for symbol {marketSignal.Symbol}.");
                return false;
            }

            BybitSpotOrderV3 placedOrder = await _tradingManager.GetOrder(order.Id);

            if (placedOrder == null)
            {
                _logger.Error($"Failed to get created market signal order for symbol {marketSignal.Symbol}.");
                return false;
            }

            marketSignal.OrderReference = placedOrder;

            _logger.Debug("PLACED ORDER >>> " + placedOrder.ObjectToString());
            return true;
        }
        
        private async Task<bool> RemoveMarketSignalOrder(MarketSignal marketSignal)
        {
            if (marketSignal == null || marketSignal.OrderReference == null)
                return false;

            BybitSpotOrderV3 order = new BybitSpotOrderV3();
            order.Symbol = marketSignal.Symbol;
            order.Quantity = marketSignal.MarketDirection == MarketDirection.Uptrend ? marketSignal.OrderReference.QuantityFilled : marketSignal.OrderReference.QuoteQuantity;
            order.Side = marketSignal.MarketDirection == MarketDirection.Uptrend ? OrderSide.Sell : OrderSide.Buy;

            if (!await _tradingManager.PlaceOrder(order))
            {
                _logger.Error($"Failed to remove market signal order for symbol {marketSignal.Symbol}.");
                return false;
            }

            BybitSpotOrderV3 removedOrder = await _tradingManager.GetOrder(order.Id);

            if (removedOrder == null)
            {
                _logger.Error($"Failed to get removed market signal order for symbol {marketSignal.Symbol}.");
                return false;
            }

            _logger.Debug("REMOVED ORDER >>> " + removedOrder.ObjectToString());
            return true;
        }

        private async void HandleLatestMarketSignal(string symbol)
        {
            MarketSignal latestMarketSignal = GetLatestMarketSignal(symbol);

            if (latestMarketSignal == null)
                return;

            MarketInformation latestMarketInformation = GetLatestMarketInformation(symbol);

            if (latestMarketInformation == null || latestMarketInformation.Volume == null || latestMarketInformation.Price == null)
                return;

            decimal activeBuyVolume = latestMarketInformation.Volume.GetMarketEntityWindowActiveBuyVolume();
            decimal activeSellVolume = latestMarketInformation.Volume.GetMarketEntityWindowActiveSellVolume();
            decimal passiveBuyVolume = latestMarketInformation.Volume.GetMarketEntityWindowPassiveBuyVolume();
            decimal passiveSellVolume = latestMarketInformation.Volume.GetMarketEntityWindowPassiveSellVolume();

            var volumeDirections = latestMarketInformation.Volume.GetMarketEntitySubwindowVolumeDirections();

            if (!volumeDirections.IsNullOrEmpty() && volumeDirections.TryGetValue(0, out VolumeDirection firstSubwindowVolumeDirection))
            {
                if (firstSubwindowVolumeDirection == VolumeDirection.Unknown)
                    return;

                var priceChangeDirections = latestMarketInformation.Price.GetMarketEntitySubwindowPriceDirections();

                if (!priceChangeDirections.IsNullOrEmpty() && priceChangeDirections.TryGetValue(0, out PriceDirection firstSubwindowPriceChangeDirection))
                {
                    if (firstSubwindowPriceChangeDirection == PriceDirection.Unknown)
                        return;

                    bool removeMarketSignal = false;

                    if (latestMarketSignal.MarketDirection == MarketDirection.Uptrend)
                    {
                        if (firstSubwindowVolumeDirection == VolumeDirection.Sell && firstSubwindowPriceChangeDirection == PriceDirection.Down)
                        {
                            removeMarketSignal = true;
                        }
                        else if (activeSellVolume > activeBuyVolume * 2 || passiveSellVolume > passiveBuyVolume * 10) // testing exit
                        {
                            removeMarketSignal = true;
                        }
                    }
                    else if (latestMarketSignal.MarketDirection == MarketDirection.Downtrend)
                    {
                        if (firstSubwindowVolumeDirection == VolumeDirection.Buy && firstSubwindowPriceChangeDirection == PriceDirection.Up)
                        {
                            removeMarketSignal = true;
                        }
                        else if (activeBuyVolume > activeSellVolume * 2 || passiveBuyVolume > passiveSellVolume * 10) //xxx testing exit 
                        {
                            removeMarketSignal = true;
                        }
                    }

                    if (removeMarketSignal)
                    {
                        await RemoveMarketSignal(symbol);
                    }
                }
            }
        }


        #region Workers

        private void HandleMarketEntitesInThread()
        {
            _logger.Debug("HandleMarketEntitesInThread started.");

            try
            {
                while (true)
                {
                    foreach (string symbol in _config.Symbols)
                    {
                        if (!IsNewMarketEntityAvailable(symbol))
                            continue;

                        if (!CreateMarketInformation(symbol, out MarketInformation marketInformation))
                            continue;

                        UpdateMarketInformation(marketInformation);
                    }
                }
            }
            catch (Exception e)
            {
                _logger.Error($"Failed HandleMarketEntitesInThread. {e}");
            }
        }

        private async void HandleMarketInformationsInThread()
        {
            _logger.Debug("HandleMarketInformationsInThread started.");

            try
            {
                while (true)
                {
                    foreach (string symbol in _config.Symbols)
                    {
                        if (IsNewMarketInformationAvailable(symbol))
                        {
                            await EvaluateMarketInformations(symbol);
                        }
                    }
                }
            }
            catch (Exception e)
            {
                _logger.Error($"Failed HandleMarketInformationsInThread. {e}");
            }
        }

        private void MonitorMarketSignalsInThread()
        {
            _logger.Debug("MonitorMarketSignalsInThread started.");

            try
            {
                while (true)
                {
                    foreach (string symbol in _config.Symbols)
                    {
                        MarketSignal marketSignal = GetLatestMarketSignal(symbol);

                        if (marketSignal != null)
                        {
                            _verboseLogger.Debug(marketSignal.DumpCreated());
                        }

                        _verboseLogger.Debug($">>> TOTAL_ROI: {_totalROI}$, MIN_ROI: {_minROI}$, MAX_ROI: {_maxROI}$.");

                        Task.Delay(MARKETSIGNAL_MONITOR_DELAY).Wait();
                    }
                }
            }
            catch (Exception e)
            {
                _logger.Error($"Failed MonitorMarketSignalsInThread. {e}");
            }
        }
        
        private void MonitorWebSocketSubscriptionStates()
        {
            _logger.Debug("MonitorWebSocketSubscriptionStates started.");

            try
            {
                bool webSocketFailure = false;
                bool webSocketError = false;

                int loopCnt = 0;
                while (true)
                {
                    if (_webSocket.V5SpotApi.CurrentSubscriptions > 0)
                    {
                        if (_webSocket.V5SpotApi.IncomingKbps == 0)
                        {
                            if (webSocketError)
                                continue;

                            _verboseLogger.Warn($"Strange situation. CurrentSubscriptions: {_webSocket.V5SpotApi.CurrentSubscriptions} and IncomingKbps = 0.");

                            if (!webSocketFailure)
                            {
                                _verboseLogger.Debug("Performing socket reconnection...");

                                CloseWebSocketSubscription();

                                Thread.Sleep(500);

                                InvokeWebSocketSubscription();

                                webSocketFailure = true;
                            }
                            else
                            {
                                _verboseLogger.Debug("Performing socket refresh...");

                                RefreshWebSocketSubscription();

                                webSocketError = true;
                            }
                        }

                        webSocketFailure = false;
                        webSocketError = false;
                    }

                    if (++loopCnt % 10 == 0)
                    {
                        _verboseLogger.Info($"{_webSocket.V5SpotApi.GetSubscriptionsState()}");
                    }

                    Task.Delay(SUBSCRIPTION_STATE_DELAY).Wait();
                }
            }
            catch (Exception e)
            {
                _logger.Error($"Failed MonitorWebSocketSubscriptionStates. {e}");
            }
        }

        #endregion


        #region Create data

        private bool CreateMarketEntity(string symbol, out MarketEntity marketEntity)
        {
            marketEntity = null;

            List<BybitTrade> marketTrades = GetLatestMarketTrades(symbol);
            if (marketTrades.IsNullOrEmpty())
                return false;

            BybitOrderbook orderBook = GetLatestOrderbook(symbol);
            if (orderBook == null)
                return false;

            marketEntity = new MarketEntity(symbol);
            marketEntity.Price = marketTrades.First().Price;
            marketEntity.ActiveTrades = marketTrades;
            marketEntity.Orderbook = orderBook;

            return true;
        }

        private bool CreateMarketEntityWindow(string symbol, out List<MarketEntity> marketEntityWindow)
        {
            marketEntityWindow = null;

            if (!IsMarketEntityWindowReady(symbol))
                return false;

            try
            {
                marketEntityWindow = GetOrderedMarketEntities(symbol).Take(_config.MarketEntityWindowSize).ToList();
                return true;
            }
            finally
            {
                RemoveHistoricMarketEntity(symbol);
            }
        }

        private bool CreateMarketInformation(string symbol, out MarketInformation marketInformation)
        {
            marketInformation = null;

            if (!CreateMarketEntityWindow(symbol, out List<MarketEntity> marketEntityWindow))
                return false;

            marketInformation = new MarketInformation(symbol);

            marketInformation.Volume = new Volume(symbol);
            marketInformation.Volume.MarketEntityWindow = marketEntityWindow;
            marketInformation.Volume.Orderbook = GetLatestOrderbook(symbol);

            marketInformation.Price = new Price(symbol);
            marketInformation.Price.MarketEntityWindow = marketEntityWindow;

            return true;
        }

        private async Task<MarketSignal> CreateMarketSignal(string symbol, MarketDirection marketDirection)
        {
            if (marketDirection == MarketDirection.Unknown)
                return null;

            MarketSignal marketSignal = new MarketSignal(symbol);
            marketSignal.MarketDirection = marketDirection;
            marketSignal.EntryPrice = GetLatestTicker(symbol)?.LastPrice ?? decimal.MinValue;
            marketSignal.MarketInformation = GetLatestMarketInformation(symbol);

            if (!await CreateMarketSignalOrder(marketSignal))
                return null;

            return marketSignal;
        }

        #endregion


        #region Get latest data

        private List<BybitTrade> GetLatestMarketTrades(string symbol)
        {
            lock (_marketTradeBuffer)
            {
                if (!_marketTradeBuffer.TryGetValue(symbol, out List<BybitTrade> marketTrades))
                {
                    return Enumerable.Empty<BybitTrade>().ToList();
                }

                return marketTrades;
            }
        }

        private BybitOrderbook GetLatestOrderbook(string symbol)
        {
            lock (_orderbookBuffer)
            {
                if (!_orderbookBuffer.TryGetValue(symbol, out BybitOrderbook orderbook))
                {
                    return null;
                }

                return orderbook;
            }
        }

        private BybitSpotTickerUpdate GetLatestTicker(string symbol)
        {
            lock (_marketTickerBuffer)
            {
                if (!_marketTickerBuffer.TryGetValue(symbol, out BybitSpotTickerUpdate ticker))
                {
                    return null;
                }

                return ticker;
            }
        }

        private MarketSignal GetLatestMarketSignal(string symbol)
        {
            lock (_marketSignalBuffer)
            {
                if (!_marketSignalBuffer.TryGetValue(symbol, out MarketSignal marketSignal))
                {
                    return null;
                }

                return marketSignal;
            }
        }

        private MarketInformation GetLatestMarketInformation(string symbol)
        {
            return GetOrderedMarketInformations(symbol).FirstOrDefault();
        }

        private List<MarketEntity> GetOrderedMarketEntities(string symbol)
        {
            lock (_marketEntityCollection)
            {
                if (!_marketEntityCollection.TryGetValue(symbol, out List<MarketEntity> marketEntities))
                {
                    return Enumerable.Empty<MarketEntity>().ToList();
                }

                return marketEntities.OrderByDescending(x => x.CreatedAt).ToList();
            }
        }

        private List<MarketEntity> GetHistoricMarketEntities(string symbol)
        {
            var marketEntities = GetOrderedMarketEntities(symbol);

            if (marketEntities.IsNullOrEmpty())
            {
                return Enumerable.Empty<MarketEntity>().ToList();
            }

            return marketEntities.Skip(_config.MarketEntityWindowSize).ToList();
        }

        private List<MarketInformation> GetOrderedMarketInformations(string symbol)
        {
            lock (_marketInformationCollection)
            {
                if (!_marketInformationCollection.TryGetValue(symbol, out List<MarketInformation> marketInformations))
                {
                    return Enumerable.Empty<MarketInformation>().ToList();
                }

                return marketInformations.OrderByDescending(x => x.CreatedAt).ToList();
            }
        }

        private List<MarketInformation> GetHistoricMarketInformations(string symbol)
        {
            var marketInformations = GetOrderedMarketInformations(symbol);

            if (marketInformations.IsNullOrEmpty())
            {
                return Enumerable.Empty<MarketInformation>().ToList();
            }

            return marketInformations.Skip(_config.MarketEntityWindowSize).ToList();
        }

        #endregion


        #region Update data

        private void UpdateMarketTrades(List<BybitTrade> marketTrades)
        {
            if (marketTrades.IsNullOrEmpty())
                return;

            lock (_marketTradeBuffer)
            {
                string symbol = marketTrades.First().Symbol;

                if (!_marketTradeBuffer.TryGetValue(symbol, out _))
                {
                    _marketTradeBuffer.Add(symbol, marketTrades);
                }
                else
                {
                    _marketTradeBuffer[symbol] = marketTrades;
                }
            }
        }

        private void UpdateOrderbook(BybitOrderbook orderbook)
        {
            if (orderbook == null) 
                return;

            lock (_orderbookBuffer)
            {
                if (!_orderbookBuffer.TryGetValue(orderbook.Symbol, out BybitOrderbook orderbookEntry))
                {
                    _orderbookBuffer.Add(orderbook.Symbol, orderbook);
                    return;
                }

                if (!orderbook.Bids.IsNullOrEmpty())
                {
                    List<BybitOrderbookEntry> bids = orderbookEntry.Bids.ToList();

                    for (int i = 0; i < orderbook.Bids.Count(); i++)
                    {
                        if (i >= bids.Count())
                        {
                            // overhead detected
                            break;
                        }

                        bids[i] = orderbook.Bids.ElementAt(i);
                    }

                    orderbookEntry.Bids = bids;
                }

                if (!orderbook.Asks.IsNullOrEmpty())
                {
                    List<BybitOrderbookEntry> asks = orderbookEntry.Asks.ToList();

                    for (int i = 0; i < orderbook.Asks.Count(); i++)
                    {
                        if (i >= asks.Count())
                        {
                            // overhead detected
                            break;
                        }

                        asks[i] = orderbook.Asks.ElementAt(i);
                    }

                    orderbookEntry.Asks = asks;
                }
            }
        }

        private void UpdateMarketEntity(MarketEntity marketEntity)
        {
            if (marketEntity == null) 
                return;

            lock (_marketEntityCollection)
            {
                if (!_marketEntityCollection.TryGetValue(marketEntity.Symbol, out _))
                {
                    List<MarketEntity> marketEntities = new List<MarketEntity>() { marketEntity };

                    _marketEntityCollection.Add(marketEntity.Symbol, marketEntities);
                }
                else
                {
                    _marketEntityCollection[marketEntity.Symbol].Add(marketEntity);
                }
            }

            SetMarketEntityPendingState(marketEntity.Symbol);
        }

        private void RemoveHistoricMarketEntity(string symbol)
        {
            var historicMarketEntities = GetHistoricMarketEntities(symbol);

            if (historicMarketEntities.IsNullOrEmpty())
                return;

            lock (_marketEntityCollection)
            {
                if (_marketEntityCollection.TryGetValue(symbol, out _))
                {
                    _marketEntityCollection[symbol].RemoveAll(x => historicMarketEntities.Equals(x.Id));
                }
            }
        }

        private void UpdateMarketInformation(MarketInformation marketInformation)
        {
            if (marketInformation == null) 
                return;

            lock (_marketInformationCollection)
            {
                if (!_marketInformationCollection.TryGetValue(marketInformation.Symbol, out _))
                {
                    List<MarketInformation> marketInformations = new List<MarketInformation>() { marketInformation };

                    _marketInformationCollection.Add(marketInformation.Symbol, marketInformations);
                }
                else
                {
                    _marketInformationCollection[marketInformation.Symbol].Add(marketInformation);
                }
            }

            SetMarketInformationPendingState(marketInformation.Symbol);
        }

        private void RemoveHistoricMarketInformation(string symbol)
        {
            var historicMarketInformations = GetHistoricMarketInformations(symbol);

            if (historicMarketInformations.IsNullOrEmpty())
                return;

            lock (_marketInformationCollection)
            {
                if (_marketInformationCollection.TryGetValue(symbol, out _))
                {
                    _marketEntityCollection[symbol].RemoveAll(x => historicMarketInformations.Equals(x.Id));
                }
            }
        }

        private void UpdateMarketTicker(BybitSpotTickerUpdate ticker)
        {
            if (ticker == null) return;

            lock (_marketTickerBuffer)
            {
                if (!_marketTickerBuffer.TryGetValue(ticker.Symbol, out _))
                {
                    _marketTickerBuffer.Add(ticker.Symbol, ticker);
                }
                else
                {
                    _marketTickerBuffer[ticker.Symbol] = ticker;
                }

                _verboseLogger.Debug($"{ticker.Symbol} PRICE: {ticker.LastPrice}$");

                HandleLatestMarketSignal(ticker.Symbol);
            }
        }

        private void UpdateMarketSignal(MarketSignal marketSignal)
        {
            if (marketSignal == null) return;

            lock (_marketSignalBuffer)
            {
                if (!_marketSignalBuffer.TryGetValue(marketSignal.Symbol, out _))
                {
                    _marketSignalBuffer.Add(marketSignal.Symbol, marketSignal);
                }
                else
                {
                    _marketSignalBuffer[marketSignal.Symbol] = marketSignal;
                }
            }
        }

        private async Task RemoveMarketSignal(string symbol)
        {
            MarketSignal marketSignal;

            lock(_marketSignalBuffer)
            {
                if (_marketSignalBuffer.TryGetValue(symbol, out marketSignal))
                {
                    marketSignal.ExitPrice = GetLatestTicker(symbol)?.LastPrice ?? 0;

                    _verboseLogger.Debug(marketSignal.DumpOnRemove());

                    //xxx delete
                    _totalROI += marketSignal.ROI;

                    if (marketSignal.ROI > _maxROI)
                    {
                        _maxROI = marketSignal.ROI;
                    }
                    
                    if (marketSignal.ROI < _minROI)
                    {
                        _minROI = marketSignal.ROI;
                    }

                    _marketSignalBuffer.Remove(symbol);
                }
            }

            await RemoveMarketSignalOrder(marketSignal);
        }

        #endregion


        #region Subscription handlers

        private void HandleTicker(DataEvent<BybitSpotTickerUpdate> ticker)
        {
            try
            {
                _tickerSemaphore.WaitAsync();

                UpdateMarketTicker(ticker.Data);
            }
            catch (Exception e)
            {
                _logger.Error($"Failed HandleTicker. {e}");
            }
            finally
            {
                _tickerSemaphore.Release();
            }
        }

        private void HandleMarketTrades(DataEvent<IEnumerable<BybitTrade>> marketTrades)
        {
            try
            {
                _marketTradeSemaphore.WaitAsync();

                UpdateMarketTrades(marketTrades.Data.ToList());

                if (CreateMarketEntity(marketTrades.Topic, out MarketEntity marketEntity))
                {
                    UpdateMarketEntity(marketEntity);
                }  
            }
            catch (Exception e)
            {
                _logger.Error($"Failed HandleMarketTrades. {e}");
            }
            finally
            {
                _marketTradeSemaphore.Release();
            }
        }

        private void HandleOrderbookSnapshot(DataEvent<BybitOrderbook> orderbook)
        {
            try
            {
                _orderbookSnapshotSemaphore.WaitAsync();

                UpdateOrderbook(orderbook.Data);
            }
            catch (Exception e)
            {
                _logger.Error($"Failed HandleOrderbookSnapshot. {e}");
            }
            finally
            {
                _orderbookSnapshotSemaphore.Release();
            }
        }

        private void HandleOrderbookUpdate(DataEvent<BybitOrderbook> orderbook)
        {
            try
            {
                _orderbookUpdateSemaphore.WaitAsync();

                UpdateOrderbook(orderbook.Data);
            }
            catch (Exception e)
            {
                _logger.Error($"Failed HandleOrderbookUpdate. {e}");
            }
            finally
            {
                _orderbookUpdateSemaphore.Release();
            }
        }

        #endregion


        #region Event handlers

        private void WebSocketEventSubscription_TickerConnectionRestored(TimeSpan obj)
        {
            _logger.Info("Subscription to ticker restored.");
        }

        private void WebSocketEventSubscription_TickerConnectionLost()
        {
            _logger.Warn("Subscription to ticker lost.");
        }

        private void WebSocketEventSubscription_TickerConnectionClosed()
        {
            _logger.Info("Subscription to ticker closed.");
        }

        private void WebSocketEventSubscription_MarketTradeUpdatesConnectionRestored(TimeSpan obj)
        {
            _logger.Info("Subscription to market trade updates restored.");
        }

        private void WebSocketEventSubscription_MarketTradeUpdatesConnectionLost()
        {
            _logger.Warn("Subscription to market trade updates lost.");
        }

        private void WebSocketEventSubscription_MarketTradeUpdatesConnectionClosed()
        {
            _logger.Info("Subscription to market trade updates closed.");
        }

        private void WebSocketEventSubscription_OrderbookUpdatesConnectionRestored(TimeSpan obj)
        {
            _logger.Info("Subscription to orderbook updates restored.");
        }

        private void WebSocketEventSubscription_OrderbookUpdatesConnectionLost()
        {
            _logger.Warn("Subscription to orderbook updates lost.");
        }

        private void WebSocketEventSubscription_OrderbookUpdatesConnectionClosed()
        {
            _logger.Info("Subscription to orderbook updates closed.");
        }

        #endregion
    }
}
