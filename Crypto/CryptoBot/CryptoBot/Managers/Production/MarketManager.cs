using Bybit.Net;
using Bybit.Net.Clients;
using Bybit.Net.Enums;
using Bybit.Net.Objects;
using Bybit.Net.Objects.Models;
using Bybit.Net.Objects.Models.Derivatives;
using Bybit.Net.Objects.Models.Socket;
using Bybit.Net.Objects.Models.Socket.Derivatives;
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
        private readonly SemaphoreSlim _marketTradeSemaphore;
        private readonly SemaphoreSlim _tickerSnapshotSemaphore;
        private readonly SemaphoreSlim _tickerUpdateSemaphore;
        private readonly SemaphoreSlim _orderbookSnapshotSemaphore;
        private readonly SemaphoreSlim _orderbookUpdateSemaphore;
        private readonly BybitSocketClient _webSocket;

        private ILogger _logger;
        private ILogger _verboseLogger;
        private bool _isInitialized;
        private Dictionary<string, BybitDerivativesOrderBookEntry> _orderbooks;
        private Dictionary<string, BybitDerivativesTicker> _tickers;
        private Dictionary<string, List<MarketEntity>> _marketEntities;
        private Dictionary<string, List<MarketEvaluationWindow>> _marketEvaluationWindows;
        private Dictionary<string, List<MarketConfirmationWindow>> _marketConfirmationWindows;
        private Dictionary<string, AutoResetEvent> _marketEntityPendings;
        private Dictionary<string, MarketSignal> _marketSignals;

#if DEBUG
        decimal _totalROI = 0;
        decimal _maxROI = 0;
        decimal _minROI = 0;
#endif

        public MarketManager(LogFactory logFactory, Config config)
        {
            _logger = logFactory.GetCurrentClassLogger();
            _verboseLogger = logFactory.GetLogger("verbose");
            _config = config;

            _marketTradeSemaphore = new SemaphoreSlim(1, 1);
            _tickerSnapshotSemaphore = new SemaphoreSlim(1, 1);
            _tickerUpdateSemaphore = new SemaphoreSlim(1, 1);
            _orderbookSnapshotSemaphore = new SemaphoreSlim(1, 1);
            _orderbookUpdateSemaphore = new SemaphoreSlim(1, 1);

            _webSocket = new BybitSocketClient(optionsDelegate =>
            {
                optionsDelegate.Environment = BybitEnvironment.Live;
                optionsDelegate.AutoReconnect = true;
            });

            _isInitialized = false;
            _orderbooks = new Dictionary<string, BybitDerivativesOrderBookEntry>();
            _tickers = new Dictionary<string, BybitDerivativesTicker>();
            _marketEntities = new Dictionary<string, List<MarketEntity>>();
            _marketEvaluationWindows = new Dictionary<string, List<MarketEvaluationWindow>>();
            _marketConfirmationWindows = new Dictionary<string, List<MarketConfirmationWindow>>();
            _marketEntityPendings = new Dictionary<string, AutoResetEvent>();
            _marketSignals = new Dictionary<string, MarketSignal>();
        }

        public bool Initialize()
        {
            try
            {
                if (_isInitialized) return true;

                // run thread workers
                Task.Run(() => { HandleMarketEntitesInThread(); });
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
                SubscribeToTradeUpdatesAsync();

                SubscribeToOrderbookUpdatesAsync();

                SubscribeToTickerUpdatesAsync();
            }
            catch (Exception e)
            {
                _logger.Error($"Failed InvokeWebSocketEventSubscription. {e}");
            }
        }

        public async void RefreshWebSocketSubscription()
        {
            _logger.Info("Refreshing web socket event subscription.");

            await _webSocket.DerivativesApi.ReconnectAsync();
        }

        public async void CloseWebSocketSubscription()
        {
            _logger.Info("Closing web socket event subscription.");

            await _webSocket.DerivativesApi.UnsubscribeAllAsync();
        }

        private async void SubscribeToTradeUpdatesAsync()
        {
            _logger.Info("Subscribing to market trade updates.");

            CallResult<UpdateSubscription> response = await _webSocket.DerivativesApi.SubscribeToTradesUpdatesAsync(StreamDerivativesCategory.USDTPerp, _config.Symbols, HandleMarketTrades);

            if (!response.GetResultOrError(out UpdateSubscription updateSubscription, out Error error))
            {
                throw new Exception($"Failed to subscribe to market trade updates. Error: ({error?.Code}) {error?.Message}.");
            }

            updateSubscription.ConnectionRestored += WebSocketEventSubscription_MarketTradeUpdatesConnectionRestored;
            updateSubscription.ConnectionLost += WebSocketEventSubscription_MarketTradeUpdatesConnectionLost;
            updateSubscription.ConnectionClosed += WebSocketEventSubscription_MarketTradeUpdatesConnectionClosed;
        }

        private async void SubscribeToOrderbookUpdatesAsync()
        {
            _logger.Info("Subscribing to orderbook updates.");

            CallResult<UpdateSubscription> response = await _webSocket.DerivativesApi.SubscribeToOrderBooksUpdatesAsync(StreamDerivativesCategory.USDTPerp, _config.Symbols, 50, HandleOrderbookSnapshot, HandleOrderbookUpdate);

            if (!response.GetResultOrError(out UpdateSubscription updateSubscription, out Error error))
            {
                throw new Exception($"Failed to subscribe to orderbook updates. Error: ({error?.Code}) {error?.Message}.");
            }

            updateSubscription.ConnectionRestored += WebSocketEventSubscription_OrderbookUpdatesConnectionRestored;
            updateSubscription.ConnectionLost += WebSocketEventSubscription_OrderbookUpdatesConnectionLost;
            updateSubscription.ConnectionClosed += WebSocketEventSubscription_OrderbookUpdatesConnectionClosed;
        }

        private async void SubscribeToTickerUpdatesAsync()
        {
            _logger.Info("Subscribing to ticker updates.");

            CallResult<UpdateSubscription> response = await _webSocket.DerivativesApi.SubscribeToTickersUpdatesAsync(StreamDerivativesCategory.USDTPerp, _config.Symbols, HandleTickerSnapshot, HandleTickerUpdate);

            if (!response.GetResultOrError(out UpdateSubscription updateSubscription, out Error error))
            {
                throw new Exception($"Failed to subscribe to ticker updates. Error: ({error?.Code}) {error?.Message}.");
            }

            updateSubscription.ConnectionRestored += WebSocketEventSubscription_TickerConnectionRestored;
            updateSubscription.ConnectionLost += WebSocketEventSubscription_TickerConnectionLost;
            updateSubscription.ConnectionClosed += WebSocketEventSubscription_TickerConnectionClosed;
        }

        private bool IsMarketEvaluationWindowReady(string symbol)
        {
            return GetOrderedMarketEntities(symbol).Count() >= _config.MarketEvaluationWindowSize;
        }

        private bool IsMarketConfirmationWindowReady(string symbol)
        {
            return GetOrderedMarketEntities(symbol).Count() >= _config.MarketEvaluationWindowSize + _config.MarketConfirmationWindowSize;
        }

        private bool IsMarketEntityAvailable(string symbol)
        {
            lock (_marketEntityPendings)
            {
                if (!_marketEntityPendings.TryGetValue(symbol, out AutoResetEvent marketEntityState))
                    return false;

                return marketEntityState.WaitOne(0);
            }
        }

        private void SetMarketEntityPendingState(string symbol)
        {
            lock (_marketEntityPendings)
            {
                if (!_marketEntityPendings.TryGetValue(symbol, out _))
                {
                    AutoResetEvent marketEntityPendingState = new AutoResetEvent(true);

                    _marketEntityPendings.Add(symbol, marketEntityPendingState);
                }
                else
                {
                    _marketEntityPendings[symbol].Set();
                }
            }
        }

        private void InvokeMarketEntry(string symbol, MarketEvaluationWindow marketEvaluationWindow, MarketConfirmationWindow marketConfirmationWindow)
        {
            if (marketEvaluationWindow == null || marketConfirmationWindow == null)
                return;

            _verboseLogger.Debug($"Invoking market entry on market windows:\n {marketEvaluationWindow.Dump()} {marketConfirmationWindow.Dump()}");

            MarketDirection marketEvaluationWindowMarketDirection = marketEvaluationWindow.GetMarketDirection();
            MarketDirection marketConfirmationWindowMarketDirection = marketConfirmationWindow.GetMarketDirection();

            if (marketEvaluationWindowMarketDirection != MarketDirection.Unknown)
            {
                if (marketEvaluationWindowMarketDirection == marketConfirmationWindowMarketDirection)
                {
                    decimal marketEvaluationWindowAveragePrice = marketEvaluationWindow.GetAveragePrice();
                    decimal marketConfirmationWindowAveragePrice = marketConfirmationWindow.GetAveragePrice();

                    if (marketEvaluationWindowMarketDirection == MarketDirection.Uptrend)
                    {
                        if (marketConfirmationWindowAveragePrice > marketEvaluationWindowAveragePrice)
                        {
                            CreateMarketSignal(symbol, MarketDirection.Downtrend);
                        }
                    }
                    else if (marketEvaluationWindowMarketDirection == MarketDirection.Downtrend)
                    {
                        if (marketConfirmationWindowAveragePrice < marketEvaluationWindowAveragePrice)
                        {
                            CreateMarketSignal(symbol, MarketDirection.Uptrend);
                        }
                    }
                }
            }
        }

        private void HandleMarketSignal(MarketSignal marketSignal)
        {
            if (marketSignal == null) return;

            BybitDerivativesTicker ticker = GetLatestTicker(marketSignal.Symbol);

            if (ticker == null) return;

            bool removeMarketSignal = false;

            if (marketSignal.MarketDirection ==  MarketDirection.Uptrend)
            {
                if (ticker.LastPrice >= marketSignal.TakeProfit)
                {
                    marketSignal.ExitPrice = ticker.LastPrice;
                    removeMarketSignal = true;
                }
                else if (ticker.LastPrice <= marketSignal.StopLoss)
                {
                    marketSignal.ExitPrice = ticker.LastPrice;
                    removeMarketSignal = true;
                }
            }
            else if (marketSignal.MarketDirection == MarketDirection.Downtrend)
            {
                if (ticker.LastPrice <= marketSignal.TakeProfit)
                {
                    marketSignal.ExitPrice = ticker.LastPrice;
                    removeMarketSignal = true;
                }
                else if (ticker.LastPrice >= marketSignal.StopLoss)
                {
                    marketSignal.ExitPrice = ticker.LastPrice;
                    removeMarketSignal = true;
                }
            }

            if (removeMarketSignal)
            { 
                RemoveMarketSignal(marketSignal.Symbol);

#if DEBUG
                _totalROI += marketSignal.ROI;

                if (marketSignal.ROI > _maxROI)
                {
                    _maxROI = marketSignal.ROI;
                }

                if (marketSignal.ROI < _minROI)
                {
                    _minROI = marketSignal.ROI;
                }
#endif
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
                        if (!IsMarketEntityAvailable(symbol))
                            continue;

                        if (CreateMarketEvaluationWindow(symbol, out MarketEvaluationWindow marketEvaluationWindow))
                        {
                            if (CreateMarketConfirmationWindow(symbol, out MarketConfirmationWindow marketConfirmationWindow))
                            {
                                InvokeMarketEntry(symbol, marketEvaluationWindow, marketConfirmationWindow);
                            }

                            SaveMarketEvaluationWindow(marketEvaluationWindow);

                            RemoveOldMarketEntities(symbol);
                        }
                    }
                }
            }
            catch (Exception e)
            {
                _logger.Error($"Failed HandleMarketEntitesInThread. {e}");
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
                        HandleMarketSignal(GetLatestMarketSignal(symbol));

#if DEBUG
                        _verboseLogger.Info($">>> TOTAL_ROI: {_totalROI}$ >>> MAX_ROI: {_maxROI}$ >>> MIN_ROI: {_minROI}$ >>>");

#endif
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

        private bool CreateMarketEntity(string symbol, List<BybitDerivativesTradeUpdate> marketTrades, out MarketEntity marketEntity)
        {
            marketEntity = null;

            if (marketTrades.IsNullOrEmpty())
                return false;

            BybitDerivativesOrderBookEntry orderbook = GetLatestOrderbook(symbol);

            if (orderbook == null)
                return false;

            marketEntity = new MarketEntity(symbol);
            marketEntity.Price = marketTrades.First().Price;
            marketEntity.MarketTrades = marketTrades;
            marketEntity.Orderbook = orderbook;

            return true;
        }

        private bool CreateMarketEvaluationWindow(string symbol, out MarketEvaluationWindow marketEvaluationWindow)
        {
            marketEvaluationWindow = null;

            if (!IsMarketEvaluationWindowReady(symbol))
                return false;

            MarketEvaluationWindow activeMarketEvaluationWindow = GetActiveMarketEvaluationWindow(symbol);

            if (activeMarketEvaluationWindow != null)
            {
                marketEvaluationWindow = activeMarketEvaluationWindow;
                return true;
            }

            marketEvaluationWindow = new MarketEvaluationWindow(symbol);
            marketEvaluationWindow.MarketEntities = GetOrderedMarketEntities(symbol).Take(_config.MarketEvaluationWindowSize).ToList();
            marketEvaluationWindow.IsActive = true;

            return true;
        }

        private bool CreateMarketConfirmationWindow(string symbol, out MarketConfirmationWindow marketConfirmationWindow)
        {
            marketConfirmationWindow = null;

            if (!IsMarketConfirmationWindowReady(symbol))
                return false;

            MarketEvaluationWindow activeMarketEvaluationWindow = GetActiveMarketEvaluationWindow(symbol);

            if (activeMarketEvaluationWindow != null)
            {
                // create new market evaluation window
                activeMarketEvaluationWindow.IsActive = false;
            }

            marketConfirmationWindow = new MarketConfirmationWindow(symbol);
            marketConfirmationWindow.MarketEntities = GetOrderedMarketEntities(symbol).Skip(_config.MarketEvaluationWindowSize).Take(_config.MarketConfirmationWindowSize).ToList();

            return true;
        }

        private void CreateMarketSignal(string symbol, MarketDirection marketDirection)
        {
            if (_marketSignals.TryGetValue(symbol, out _))
                return;

            if (marketDirection == MarketDirection.Unknown)
                return;

            BybitDerivativesTicker ticker = GetLatestTicker(symbol);

            if (ticker == null) return;

            MarketSignal marketSignal = new MarketSignal(symbol);
            marketSignal.EntryPrice = ticker.LastPrice;
            marketSignal.MarketDirection = marketDirection;

            _verboseLogger.Debug(marketSignal.DumpCreated());

            lock (_marketSignals)
            {
                _marketSignals.Add(symbol, marketSignal);
            }
        }

        #endregion


        #region Get latest data

        private MarketEvaluationWindow GetActiveMarketEvaluationWindow(string symbol)
        {
            lock (_marketEvaluationWindows)
            {
                if (!_marketEvaluationWindows.TryGetValue(symbol, out List<MarketEvaluationWindow> marketEvaluationWindows))
                {
                    return null;
                }

                return marketEvaluationWindows.FirstOrDefault(x => x.IsActive);
            }
        }

        private BybitDerivativesOrderBookEntry GetLatestOrderbook(string symbol)
        {
            lock (_orderbooks)
            {
                if (!_orderbooks.TryGetValue(symbol, out BybitDerivativesOrderBookEntry orderbook))
                {
                    return null;
                }

                return orderbook;
            }
        }

        private BybitDerivativesTicker GetLatestTicker(string symbol)
        {
            lock (_tickers)
            {
                if (!_tickers.TryGetValue(symbol, out BybitDerivativesTicker ticker))
                {
                    return null;
                }

                return ticker;
            }
        }

        private MarketSignal GetLatestMarketSignal(string symbol)
        {
            lock (_marketSignals)
            {
                if (!_marketSignals.TryGetValue(symbol, out MarketSignal marketSignal))
                {
                    return null;
                }

                return marketSignal;
            }
        }

        private List<MarketEntity> GetOrderedMarketEntities(string symbol)
        {
            lock (_marketEntities)
            {
                if (!_marketEntities.TryGetValue(symbol, out List<MarketEntity> marketEntities))
                {
                    return Enumerable.Empty<MarketEntity>().ToList();
                }

                return marketEntities.OrderByDescending(x => x.CreatedAt).ToList();
            }
        }

        private List<MarketEntity> GetOldMarketEntities(string symbol)
        {
            var marketEntities = GetOrderedMarketEntities(symbol);

            if (marketEntities.IsNullOrEmpty())
            {
                return Enumerable.Empty<MarketEntity>().ToList();
            }

            return marketEntities.Skip(_config.MarketEvaluationWindowSize).ToList();
        }

        #endregion


        #region Update data

        private void UpdateOrderbook(string symbol, BybitDerivativesOrderBookEntry orderbook)
        {
            if (orderbook == null) return;

            lock (_orderbooks)
            {
                if (!_orderbooks.TryGetValue(symbol, out BybitDerivativesOrderBookEntry o))
                {
                    _orderbooks.Add(symbol, orderbook);
                    return;
                }

                if (!orderbook.Bids.IsNullOrEmpty())
                {
                    List<BybitUnifiedMarginOrderBookItem> bids = o.Bids.ToList();

                    for (int i = 0; i < orderbook.Bids.Count(); i++)
                    {
                        if (i >= bids.Count())
                        {
                            // overhead detected
                            break;
                        }

                        bids[i] = orderbook.Bids.ElementAt(i);
                    }

                    o.Bids = bids;
                }

                if (!orderbook.Asks.IsNullOrEmpty())
                {
                    List<BybitUnifiedMarginOrderBookItem> asks = o.Asks.ToList();

                    for (int i = 0; i < orderbook.Asks.Count(); i++)
                    {
                        if (i >= asks.Count())
                        {
                            // overhead detected
                            break;
                        }

                        asks[i] = orderbook.Asks.ElementAt(i);
                    }

                    o.Asks = asks;
                }
            }
        }

        private void UpdateTicker<T>(string symbol, T ticker)
        {
            if (ticker == null) return;

            lock (_tickers)
            {
                if (!_tickers.TryGetValue(symbol, out BybitDerivativesTicker t))
                {
                    if (ticker is BybitDerivativesTicker t1)
                    {
                        _tickers.Add(symbol, t1);
                    }
                }
                else if (ticker is BybitDerivativesTickerUpdate tu)
                {
                    t.LastTickDirection = tu.TickDirection;
                    t.PriceChangePercentage24H = tu.PriceChangePercentage24H;
                    t.Turnover24H = tu.Turnover24H;
                    t.Volume24H = tu.Volume24H;
                    t.FundingRate = tu.FundingRate;
                    t.NextFundingTime = tu.NextFundingTime;
                    t.BestBidPrice = tu.Bid1Price ?? 0;
                    t.BidSize = tu.Bid1Size ?? 0;
                    t.BestAskPrice = tu.Ask1Price ?? 0;
                    t.AskSize = tu.Ask1Size ?? 0;

                    if (tu.LastPrice > 0)
                    {
                        t.LastPrice = tu.LastPrice;
                    }

                    _tickers[symbol] = t;
                }

                _verboseLogger.Debug($"{symbol} PRICE: {_tickers[symbol].LastPrice}$");

                //HandleLatestMarketSignal(ticker.Symbol);
            }
        }

        private void SaveMarketEntity(MarketEntity marketEntity)
        {
            if (marketEntity == null) return;

            lock (_marketEntities)
            {
                if (!_marketEntities.TryGetValue(marketEntity.Symbol, out _))
                {
                    List<MarketEntity> marketEntities = new List<MarketEntity>() { marketEntity };

                    _marketEntities.Add(marketEntity.Symbol, marketEntities);
                }
                else
                {
                    _marketEntities[marketEntity.Symbol].Add(marketEntity);
                }
            }

            SetMarketEntityPendingState(marketEntity.Symbol);
        }

        private void SaveMarketEvaluationWindow(MarketEvaluationWindow marketEvaluationWindow)
        {
            if (marketEvaluationWindow == null)
                return;

            lock (_marketEvaluationWindows)
            {
                if (!_marketEvaluationWindows.TryGetValue(marketEvaluationWindow.Symbol, out _))
                {
                    List<MarketEvaluationWindow> marketEvaluationWindows = new List<MarketEvaluationWindow>() { marketEvaluationWindow };

                    _marketEvaluationWindows.Add(marketEvaluationWindow.Symbol, marketEvaluationWindows);
                }
                else
                {
                    _marketEvaluationWindows[marketEvaluationWindow.Symbol].Add(marketEvaluationWindow);
                }
            }
        }

        private void RemoveOldMarketEntities(string symbol)
        {
            List<MarketEntity> oldMarketEntities = GetOldMarketEntities(symbol);

            if (oldMarketEntities.IsNullOrEmpty())
                return;

            lock (_marketEntities)
            {
                if (_marketEntities.TryGetValue(symbol, out _))
                {
                    int removedItems = _marketEntities[symbol].RemoveAll(x => oldMarketEntities.Equals(x.Id));

                    //_logger.Debug($"Removed {removedItems} old market entities for symbol {symbol}.");
                }
            }
        }

        private void RemoveMarketSignal(string symbol)
        {
            MarketSignal marketSignal;

            lock (_marketSignals)
            {
                if (_marketSignals.TryGetValue(symbol, out marketSignal))
                {
                    _verboseLogger.Debug(marketSignal.DumpOnRemove());

                    _marketSignals.Remove(symbol);
                }
            }
        }

        #endregion


        #region Subscription handlers

        private void HandleMarketTrades(DataEvent<IEnumerable<BybitDerivativesTradeUpdate>> marketTrades)
        {
            try
            {
                _marketTradeSemaphore.WaitAsync();

                if (!CreateMarketEntity(marketTrades.Topic, marketTrades.Data.ToList(), out MarketEntity marketEntity))
                {
                    _logger.Warn($"Failed to create {marketTrades.Topic} market entity.");
                }

                SaveMarketEntity(marketEntity);
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

        private void HandleTickerSnapshot(DataEvent<BybitDerivativesTicker> ticker)
        {
            try
            {
                _tickerSnapshotSemaphore.WaitAsync();

                UpdateTicker(ticker.Topic, ticker.Data);
            }
            catch (Exception e)
            {
                _logger.Error($"Failed HandleTickerSnapshot. {e}");
            }
            finally
            {
                _tickerSnapshotSemaphore.Release();
            }
        }

        private void HandleTickerUpdate(DataEvent<BybitDerivativesTickerUpdate> ticker)
        {
            try
            {
                _tickerUpdateSemaphore.WaitAsync();

                UpdateTicker(ticker.Topic, ticker.Data);
            }
            catch (Exception e)
            {
                _logger.Error($"Failed HandleTickerUpdate. {e}");
            }
            finally
            {
                _tickerUpdateSemaphore.Release();
            }
        }

        private void HandleOrderbookSnapshot(DataEvent<BybitDerivativesOrderBookEntry> orderbook)
        {
            try
            {
                _orderbookSnapshotSemaphore.WaitAsync();

                UpdateOrderbook(orderbook.Topic, orderbook.Data);
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

        private void HandleOrderbookUpdate(DataEvent<BybitDerivativesOrderBookEntry> orderbook)
        {
            try
            {
                _orderbookUpdateSemaphore.WaitAsync();

                UpdateOrderbook(orderbook.Topic, orderbook.Data);
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
