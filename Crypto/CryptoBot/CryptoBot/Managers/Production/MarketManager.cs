using Bybit.Net;
using Bybit.Net.Clients;
using Bybit.Net.Objects;
using Bybit.Net.Objects.Models.V5;
using Bybit.Net.Objects.Options;
using CryptoBot.EventArgs;
using CryptoBot.Interfaces.Managers;
using CryptoBot.Models;
using CryptoExchange.Net.Objects;
using CryptoExchange.Net.Sockets;
using NLog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CryptoBot.Managers.Production
{
    public class MarketManager : IMarketManager
    {
        private const int TEST_BALANCE_DELAY = 5000;

        private readonly Config _config;
        private readonly SemaphoreSlim _tickerSemaphore;
        private readonly SemaphoreSlim _marketTradeSemaphore;
        private readonly SemaphoreSlim _orderbookSnapshotSemaphore;
        private readonly SemaphoreSlim _orderbookUpdateSemaphore;
        private readonly BybitSocketClient _webSocket;

        private decimal _testBalance = 0;
        private decimal _testNegativeBalance = 0;
        private decimal _testPositiveBalance = 0;
        private int _wins = 0;
        private int _losses = 0;

        private ILogger _logger;
        private ILogger _verboseLogger;
        private bool _isInitialized;
        private Dictionary<string, List<BybitTrade>> _marketTradeBuffer;
        private Dictionary<string, BybitOrderbook> _orderbookBuffer;
        private Dictionary<string, AutoResetEvent> _marketEntityPendingStateBuffer;
        private Dictionary<string, AutoResetEvent> _marketInformationPendingStateBuffer;
        private Dictionary<string, BybitSpotTickerUpdate> _marketTickerBuffer;
        private Dictionary<string, MarketSignal> _marketSignalBuffer;
        private Dictionary<string, List<MarketEntity>> _marketEntityCollection;
        private Dictionary<string, List<MarketInformation>> _marketInformationCollection;
        private MarketEntityWindowVolumeSetting _volumeSetting;
        private MarketEntityWindowPriceChangeSetting _priceChangeSetting;

        /// <summary>
        /// For triggering special application events to outside world.
        /// </summary>
        public event EventHandler<ApplicationEventArgs> ApplicationEvent;

        public MarketManager(LogFactory logFactory, Config config)
        {
            _config = config;

            _logger = logFactory.GetCurrentClassLogger();
            _verboseLogger = logFactory.GetLogger("verbose");
            _tickerSemaphore = new SemaphoreSlim(1, 1);
            _marketTradeSemaphore = new SemaphoreSlim(1, 1);
            _orderbookSnapshotSemaphore = new SemaphoreSlim(1, 1);
            _orderbookUpdateSemaphore = new SemaphoreSlim(1, 1);

            _webSocket = new BybitSocketClient(optionsDelegate => { optionsDelegate.Environment = BybitEnvironment.Live; });

            _isInitialized = false;
            _orderbookBuffer = new Dictionary<string, BybitOrderbook>();
            _marketTradeBuffer = new Dictionary<string, List<BybitTrade>>();
            _marketEntityPendingStateBuffer = new Dictionary<string, AutoResetEvent>();
            _marketInformationPendingStateBuffer = new Dictionary<string, AutoResetEvent>();
            _marketTickerBuffer = new Dictionary<string, BybitSpotTickerUpdate>();
            _marketSignalBuffer = new Dictionary<string, MarketSignal>();
            _marketEntityCollection = new Dictionary<string, List<MarketEntity>>();
            _marketInformationCollection = new Dictionary<string, List<MarketInformation>>();

            CreateMarketEntityWindowSettings();
        }

        public bool Initialize()
        {
            try
            {
                if (_isInitialized) return true;

                // run workers
                Task.Run(() => { HandleMarketEntitesInThread(); });
                Task.Run(() => { HandleMarketInformationsInThread(); });
                Task.Run(() => { MonitorTestBalanceInThread(); });

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

        public void InvokeWebSocketEventSubscription()
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

        public async void CloseWebSocketEventSubscription()
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

        private void CreateMarketEntityWindowSettings()
        {
            if (_config == null)
            {
                _logger.Error("Failed to create market entity window instructions. Missing configuration.");
                return;
            }

            _volumeSetting = new MarketEntityWindowVolumeSetting();
            _volumeSetting.Subwindows = _config.Subwindows;
            _volumeSetting.OrderbookDepth = _config.OrderbookDepth;
            _volumeSetting.BuyVolumesPercentageLimit = _config.BuyVolumesPercentageLimit;
            _volumeSetting.SellVolumesPercentageLimit = _config.SellVolumesPercentageLimit;

            _priceChangeSetting = new MarketEntityWindowPriceChangeSetting();
            _priceChangeSetting.Subwindows = _config.Subwindows;
            _priceChangeSetting.UpPriceChangePercentageLimit = _config.UpPriceChangePercentageLimit;
            _priceChangeSetting.DownPriceChangePercentageLimit = _config.DownPriceChangePercentageLimit;
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

                        if (!CreateMarketInformationInstance(symbol, out MarketInformation marketInformation))
                            continue;

                        UpdateMarketInformationCollection(marketInformation);
                    }
                }
            }
            catch (Exception e)
            {
                _logger.Error($"Failed HandleMarketEntitesInThread. {e}");
            }
        }

        private void HandleMarketInformationsInThread()
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
                            EvaluateMarketInformations(symbol);
                        }
                    }
                }
            }
            catch (Exception e)
            {
                _logger.Error($"Failed HandleMarketInformationsInThread. {e}");
            }
        }

        private void MonitorTestBalanceInThread()
        {
            _logger.Debug("MonitorTestBalanceInThread started.");

            try
            {
                while (true)
                {
                    _verboseLogger.Debug($"BALANCE: {_testBalance}$ (+BALANCE: {_testPositiveBalance}$, -BALANCE: {_testNegativeBalance}$). Wins: {_wins}, losses: {_losses}, TOTAL: {_wins + _losses}");
                        
                    Task.Delay(TEST_BALANCE_DELAY).Wait();
                }
            }
            catch (Exception e)
            {
                _logger.Error($"Failed MonitorTestBalanceInThread. {e}");
            }
        }

        #endregion

        private bool IsMarketEntityWindowReady(string symbol)
        {
            return GetOrderedMarketEntityCollection(symbol).Count() >= _config.MarketEntityWindowSize;
        }

        private bool IsMarketInformationWindowReady(string symbol)
        {
            return GetOrderedMarketInformationCollection(symbol).Count() >= _config.MarketInformationWindowSize;
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

        private void EvaluateMarketInformations(string symbol)
        {
            try
            {
                if (!CrosscheckMarketInformationComponents(symbol, out MarketDirection marketDirection))
                    return;

                MarketSignal latestMarketSignal = GetLatestMarketSignal(symbol);
                if (latestMarketSignal != null)
                {
                    if (latestMarketSignal.MarketDirection == marketDirection)
                        return;

                    RemoveMarketSignalEntry(symbol);
                }

                if (!CreateMarketSignal(symbol, marketDirection, out MarketSignal marketSignal))
                    return;

                UpdateMarketSignalBuffer(marketSignal);
            }
            finally
            {
                RemoveHistoricMarketInformationCollection(symbol);
            }
        }

        private bool CrosscheckMarketInformationComponents(string symbol, out MarketDirection marketDirection)
        {
            // for now only volume component is crosschecked 

            marketDirection = MarketDirection.Unknown;

            if (!IsMarketInformationWindowReady(symbol))
                return false;

            MarketInformation referenceMarketInformation = GetLatestMarketInformationEntry(symbol);
            if (referenceMarketInformation == null)
                return false;

            MarketDirection referenceMarketDirection = referenceMarketInformation.PreferedMarketDirection;

            if (referenceMarketDirection == MarketDirection.Unknown)
            {
                _logger.Info($"Unable to determine reference market direction for symbol {symbol}.");
                return false;
            }

            _logger.Debug($"Cross check of market information {referenceMarketInformation.Id} to confirm {referenceMarketDirection} reference market direction for symbol {symbol}.");

            // all of previous market informations has to be in other directions with sign of growing counter volume

            //foreach (var marketInformation in marketInformations.Skip(1))
            //{
            //    MarketDirection currentMarketDirection = marketInformation.PreferedMarketDirection;

            //    if (currentMarketDirection == MarketDirection.Unknown)
            //    {
            //        return false;
            //    }
            //    else if (currentMarketDirection == referenceMarketDirection)
            //    {
            //        return false;
            //    }
            //}

            marketDirection = referenceMarketDirection;
            return true;
        }

        private void HandleLatestMarketSignal(string symbol)
        {
            //xxxx
            MarketInformation latestMarketInformation = GetLatestMarketInformationEntry(symbol);

            if (latestMarketInformation == null)
            {
                return;
            }

            _verboseLogger.Debug(latestMarketInformation);

            MarketSignal latestMarketSignal = GetLatestMarketSignal(symbol);

            if (latestMarketSignal == null)
            {
                return;
            }

            if (latestMarketInformation.MarketVolumeComponent != null)
            {
                decimal activeBuyVolume = latestMarketInformation.MarketVolumeComponent.GetActiveBuyVolume();
                decimal activeSellVolume = latestMarketInformation.MarketVolumeComponent.GetActiveSellVolume();
                decimal passiveBuyVolume = latestMarketInformation.MarketVolumeComponent.GetPassiveBuyVolume();
                decimal passiveSellVolume = latestMarketInformation.MarketVolumeComponent.GetPassiveSellVolume();

                if (latestMarketInformation.MarketPriceComponent != null)
                {
                    var volumeDirections = latestMarketInformation.MarketVolumeComponent.GetMarketEntitySubwindowVolumeDirections();

                    if (!volumeDirections.IsNullOrEmpty() && volumeDirections.TryGetValue(0, out VolumeDirection firstSubwindowVolumeDirection))
                    {
                        if (firstSubwindowVolumeDirection == VolumeDirection.Unknown)
                            return;

                        var priceChangeDirections = latestMarketInformation.MarketPriceComponent.GetMarketEntitySubwindowPriceChangeDirections();

                        if (!priceChangeDirections.IsNullOrEmpty() && priceChangeDirections.TryGetValue(0, out PriceChangeDirection firstSubwindowPriceChangeDirection))
                        {
                            if (firstSubwindowPriceChangeDirection == PriceChangeDirection.Unknown)
                                return;

                            bool removeMarketSignal = false;

                            if (latestMarketSignal.MarketDirection == MarketDirection.Uptrend)
                            {
                                if (firstSubwindowVolumeDirection == VolumeDirection.Sell && firstSubwindowPriceChangeDirection == PriceChangeDirection.Down)
                                {
                                    removeMarketSignal = true;
                                }
                            }
                            else if (latestMarketSignal.MarketDirection == MarketDirection.Downtrend)
                            {
                                if (firstSubwindowVolumeDirection == VolumeDirection.Buy && firstSubwindowPriceChangeDirection == PriceChangeDirection.Up)
                                {
                                    removeMarketSignal = true;
                                }
                            }

                            if (removeMarketSignal)
                            {
                                RemoveMarketSignalEntry(symbol);
                            }
                        }
                    }
                }
            }
        }

           
        #region Create data model

        private bool CreateMarketEntity(string symbol, out MarketEntity marketEntity)
        {
            marketEntity = null;

            List<BybitTrade> marketTrades = GetLatestMarketTradesEntry(symbol);
            if (marketTrades.IsNullOrEmpty())
                return false;

            BybitOrderbook orderBook = GetLatestOrderbookEntry(symbol);
            if (orderBook == null)
                return false;

            marketEntity = new MarketEntity(symbol);
            marketEntity.Price = marketTrades.First().Price;
            marketEntity.MarketTrades = marketTrades;
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
                marketEntityWindow = GetOrderedMarketEntityCollection(symbol).Take(_config.MarketEntityWindowSize).ToList();
                return true;
            }
            finally
            {
                RemoveHistoricMarketEntityCollection(symbol);
            }
        }

        private bool CreateMarketInformationInstance(string symbol, out MarketInformation marketInformation)
        {
            marketInformation = null;

            if (!CreateMarketEntityWindow(symbol, out List<MarketEntity> marketEntityWindow))
                return false;

            if (!CreateMarketVolumeComponent(symbol, marketEntityWindow, out MarketVolumeComponent marketVolumeComponent))
                return false;

            if (!CreateMarketPriceComponent(symbol, marketEntityWindow, out MarketPriceComponent marketPriceComponent))
                return false;

            // both component should be pointing at the same thing. BUY volume -> price increases totally and partially on same levels or with delay???

            marketInformation = new MarketInformation(symbol);
            marketInformation.MarketVolumeComponent = marketVolumeComponent;
            marketInformation.MarketPriceComponent = marketPriceComponent;

            return true;
        }

        private bool CreateMarketVolumeComponent(string symbol, List<MarketEntity> marketEntityWindow, out MarketVolumeComponent marketVolumeComponent)
        {
            marketVolumeComponent = null;

            if (marketEntityWindow.IsNullOrEmpty())
                return false;

            marketVolumeComponent = new MarketVolumeComponent(symbol, _volumeSetting);
            marketVolumeComponent.MarketEntityWindow = marketEntityWindow;
            return true;
        }

        private bool CreateMarketPriceComponent(string symbol, List<MarketEntity> marketEntityWindow, out MarketPriceComponent marketPriceComponent)
        {
            marketPriceComponent = null;

            if (marketEntityWindow.IsNullOrEmpty())
                return false;

            marketPriceComponent = new MarketPriceComponent(symbol, _priceChangeSetting);
            marketPriceComponent.MarketEntityWindow = marketEntityWindow;
            return true;
        }

        private bool CreateMarketSignal(string symbol, MarketDirection marketDirection, out MarketSignal marketSignal)
        {
            marketSignal = null;

            if (marketDirection == MarketDirection.Unknown)
                return false;

            marketSignal = new MarketSignal(symbol);
            marketSignal.MarketDirection = marketDirection;
            marketSignal.EntryPrice = GetLatestTickerEntry(symbol)?.LastPrice ?? decimal.MinValue;
            marketSignal.MarketInformation = GetLatestMarketInformationEntry(symbol);

            _verboseLogger.Debug(marketSignal.DumpOnCreate());
            return true;
        }

        #endregion


        #region Get data model

        private List<BybitTrade> GetLatestMarketTradesEntry(string symbol)
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

        private BybitOrderbook GetLatestOrderbookEntry(string symbol)
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

        private BybitSpotTickerUpdate GetLatestTickerEntry(string symbol)
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

        private MarketInformation GetLatestMarketInformationEntry(string symbol)
        {
            return GetOrderedMarketInformationCollection(symbol).FirstOrDefault();
        }

        private List<MarketEntity> GetOrderedMarketEntityCollection(string symbol)
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

        private List<MarketEntity> GetHistoricMarketEntityCollection(string symbol)
        {
            var marketEntities = GetOrderedMarketEntityCollection(symbol);
            if (marketEntities.IsNullOrEmpty())
            {
                return Enumerable.Empty<MarketEntity>().ToList();
            }

            return marketEntities.Skip(_config.MarketEntityWindowSize).ToList();
        }

        private List<MarketInformation> GetOrderedMarketInformationCollection(string symbol)
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

        private List<MarketInformation> GetHistoricMarketInformationCollection(string symbol)
        {
            var marketInformations = GetOrderedMarketInformationCollection(symbol);
            if (marketInformations.IsNullOrEmpty())
            {
                return Enumerable.Empty<MarketInformation>().ToList();
            }

            return marketInformations.Skip(_config.MarketEntityWindowSize).ToList();
        }

        #endregion


        #region Update data model

        private void UpdateMarketTradesEntry(List<BybitTrade> marketTrades)
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

        private void UpdateOrderbookEntry(BybitOrderbook orderbook)
        {
            if (orderbook == null) return;

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

        private void UpdateMarketEntityCollection(MarketEntity marketEntity)
        {
            if (marketEntity == null) return;

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

        private void RemoveHistoricMarketEntityCollection(string symbol)
        {
            var historicMarketEntities = GetHistoricMarketEntityCollection(symbol);
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

        private void UpdateMarketInformationCollection(MarketInformation marketInformation)
        {
            if (marketInformation == null) return;

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

        private void RemoveHistoricMarketInformationCollection(string symbol)
        {
            var historicMarketInformations = GetHistoricMarketInformationCollection(symbol);
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

        private void UpdateMarketTickerBuffer(BybitSpotTickerUpdate ticker)
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

        private void UpdateMarketSignalBuffer(MarketSignal marketSignal)
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

        private void RemoveMarketSignalEntry(string symbol)
        {
            lock(_marketSignalBuffer)
            {
                if (_marketSignalBuffer.TryGetValue(symbol, out MarketSignal marketSignal))
                {
                    marketSignal.ExitPrice = GetLatestTickerEntry(symbol)?.LastPrice ?? decimal.MinValue; // LOG IT!

                    decimal latestMarketSignalROI = marketSignal.ROI;

                    if (latestMarketSignalROI > 0)
                    {
                        _wins++;
                        _testPositiveBalance += latestMarketSignalROI;
                    }
                    else if (latestMarketSignalROI < 0)
                    {
                        _losses++;
                        _testNegativeBalance += latestMarketSignalROI;
                    }

                    _testBalance += latestMarketSignalROI;

                    _verboseLogger.Debug(marketSignal.DumpOnRemove());

                    _marketSignalBuffer.Remove(symbol);
                }
            }
        }

        #endregion


        #region Subscription handlers

        private void HandleTicker(DataEvent<BybitSpotTickerUpdate> ticker)
        {
            try
            {
                _tickerSemaphore.WaitAsync();

                //TODO:
                // implement exit rule -> [0] volume and price should point to different direction as original signal
                // in that case go to opposite direction OR wait for counter signal

                UpdateMarketTickerBuffer(ticker.Data);
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

                UpdateMarketTradesEntry(marketTrades.Data.ToList());

                if (CreateMarketEntity(marketTrades.Topic, out MarketEntity marketEntity))
                {
                    UpdateMarketEntityCollection(marketEntity);
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

                UpdateOrderbookEntry(orderbook.Data);
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

                UpdateOrderbookEntry(orderbook.Data);
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
