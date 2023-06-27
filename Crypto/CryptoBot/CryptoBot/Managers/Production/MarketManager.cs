using Bybit.Net.Clients;
using Bybit.Net.Objects;
using Bybit.Net.Objects.Models.V5;
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
        private readonly Config _config;
        private readonly SemaphoreSlim _tickerSemaphore;
        private readonly SemaphoreSlim _marketTradeSemaphore;
        private readonly SemaphoreSlim _orderbookSnapshotSemaphore;
        private readonly SemaphoreSlim _orderbookUpdateSemaphore;
        private readonly BybitSocketClient _webSocket;

        private ILogger _logger;
        private ILogger _verboseLogger;
        private bool _isInitialized;

        private MarketEntityWindowVolumeSetting _volumeSetting;
        private MarketEntityWindowPriceChangeSetting _priceChangeSetting;

        private Dictionary<string, List<BybitTrade>> _marketTradeBuffer;
        private Dictionary<string, BybitOrderbook> _orderbookBuffer;
        private Dictionary<string, AutoResetEvent> _marketEntityPendingStateBuffer;
        private Dictionary<string, AutoResetEvent> _marketInformationPendingStateBuffer;
        private Dictionary<string, BybitSpotTickerUpdate> _marketTickerBuffer;
        private Dictionary<string, List<MarketEntity>> _marketEntityCollection;
        private Dictionary<string, List<MarketInformation>> _marketInformationCollection;
        private Dictionary<string, List<MarketSignal>> _marketSignalCollection;

        private CancellationTokenSource _ctsHandleMarketEntity;
        private CancellationTokenSource _ctsHandleMarketInformation;

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

            BybitSocketClientOptions webSocketOptions = BybitSocketClientOptions.Default;
            webSocketOptions.V5StreamsOptions.OutputOriginalData = true;
            webSocketOptions.V5StreamsOptions.BaseAddress = _config.SpotStreamEndpoint;
            _webSocket = new BybitSocketClient(webSocketOptions);

            _isInitialized = false;

            _orderbookBuffer = new Dictionary<string, BybitOrderbook>();
            _marketTradeBuffer = new Dictionary<string, List<BybitTrade>>();
            _marketEntityPendingStateBuffer = new Dictionary<string, AutoResetEvent>();
            _marketInformationPendingStateBuffer = new Dictionary<string, AutoResetEvent>();
            _marketTickerBuffer = new Dictionary<string, BybitSpotTickerUpdate>();
            _marketEntityCollection = new Dictionary<string, List<MarketEntity>>();
            _marketInformationCollection = new Dictionary<string, List<MarketInformation>>();
            _marketSignalCollection = new Dictionary<string, List<MarketSignal>>();

            _ctsHandleMarketEntity = new CancellationTokenSource();
            _ctsHandleMarketInformation = new CancellationTokenSource();

            CreateMarketEntityWindowSettings();
        }

        public bool Initialize()
        {
            try
            {
                if (_isInitialized) return true;

                Task.Run(() => { HandleMarketEntitesInThread(_ctsHandleMarketEntity.Token); }, _ctsHandleMarketEntity.Token);
                Task.Run(() => { HandleMarketInformationsInThread(_ctsHandleMarketInformation.Token); }, _ctsHandleMarketInformation.Token);

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

        public async void InvokeWebSocketEventSubscription()
        {
            try
            {
                if (!_isInitialized) return;

                _logger.Info("Invoking web socket event subscription.");

                CallResult<UpdateSubscription> response = await _webSocket.V5SpotStreams.SubscribeToTickerUpdatesAsync(_config.Symbols, HandleTicker);
                if (!response.GetResultOrError(out UpdateSubscription updateSubscription, out Error error))
                {
                    throw new Exception($"Failed to subscribe to ticker updates. Error: ({error?.Code}) {error?.Message}.");
                }

                updateSubscription.ConnectionRestored += WebSocketEventSubscription_TickerConnectionRestored;
                updateSubscription.ConnectionLost += WebSocketEventSubscription_TickerConnectionLost;
                updateSubscription.ConnectionClosed += WebSocketEventSubscription_TickerConnectionClosed;

                response = await _webSocket.V5SpotStreams.SubscribeToTradeUpdatesAsync(_config.Symbols, HandleMarketTrades);
                if (!response.GetResultOrError(out updateSubscription, out error))
                {
                    throw new Exception($"Failed to subscribe to trade updates. Error: ({error?.Code}) {error?.Message}.");
                }

                updateSubscription.ConnectionRestored += WebSocketEventSubscription_MarketTradeUpdatesConnectionRestored;
                updateSubscription.ConnectionLost += WebSocketEventSubscription_MarketTradeUpdatesConnectionLost;
                updateSubscription.ConnectionClosed += WebSocketEventSubscription_MarketTradeUpdatesConnectionClosed;

                response = await _webSocket.V5SpotStreams.SubscribeToOrderbookUpdatesAsync(_config.Symbols, 50, HandleOrderbookSnapshot, HandleOrderbookUpdate);
                if (!response.GetResultOrError(out updateSubscription, out error))
                {
                    throw new Exception($"Failed to subscribe to orderbook updates. Error: ({error?.Code}) {error?.Message}.");
                }

                updateSubscription.ConnectionRestored += WebSocketEventSubscription_OrderbookUpdatesConnectionRestored;
                updateSubscription.ConnectionLost += WebSocketEventSubscription_OrderbookUpdatesConnectionLost;
                updateSubscription.ConnectionClosed += WebSocketEventSubscription_OrderbookUpdatesConnectionClosed;
            }
            catch (Exception e)
            {
                _logger.Error($"Failed InvokeWebSocketEventSubscription. {e}");

                CloseWebSocketEventSubscription();
            }
        }

        public async void CloseWebSocketEventSubscription()
        {
            _logger.Info("Closing web socket event subscription.");

            await _webSocket.V5SpotStreams.UnsubscribeAllAsync();
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

        private void HandleMarketEntitesInThread(CancellationToken cancellationToken)
        {
            _logger.Debug("HandleMarketEntitesInThread started.");

            try
            {
                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();

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
            catch (OperationCanceledException)
            {
                _logger.Info("HandleMarketEntitesInThread was canceled.");
            }
            catch (Exception e)
            {
                _logger.Error($"Failed HandleMarketEntitesInThread. {e}");
            }
        }

        private void HandleMarketInformationsInThread(CancellationToken cancellationToken)
        {
            _logger.Debug("HandleMarketInformationsInThread started.");

            try
            {
                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    foreach (string symbol in _config.Symbols)
                    {
                        if (!IsNewMarketInformationAvailable(symbol))
                            continue;

                        EvaluateMarketInformations(symbol);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                _logger.Info("HandleMarketInformationsInThread was canceled.");
            }
            catch (Exception e)
            {
                _logger.Error($"Failed HandleMarketInformationsInThread. {e}");
            }
        }

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

                if (!marketEntityPendingState.WaitOne(0))
                    return false;

                return true;
            }
        }

        private bool IsNewMarketInformationAvailable(string symbol)
        {
            lock (_marketInformationPendingStateBuffer)
            {
                if (!_marketInformationPendingStateBuffer.TryGetValue(symbol, out AutoResetEvent marketInformationPendingState))
                    return false;

                if (!marketInformationPendingState.WaitOne(0))
                    return false;

                return true;
            }
        }

        private void SetMarketEntityPendingState(string symbol)
        {
            lock (_marketEntityPendingStateBuffer)
            {
                if (!_marketEntityPendingStateBuffer.TryGetValue(symbol, out _))
                {
                    AutoResetEvent marketEntityPendingState = new AutoResetEvent(false);

                    _marketEntityPendingStateBuffer.Add(symbol, marketEntityPendingState);
                }

                _marketEntityPendingStateBuffer[symbol].Set();
            }
        }

        private void SetMarketInformationPendingState(string symbol)
        {
            lock (_marketInformationPendingStateBuffer)
            {
                _logger.Debug("SetMarketInformationPendingState.");

                if (!_marketInformationPendingStateBuffer.TryGetValue(symbol, out _))
                {
                    AutoResetEvent marketInformationPendingState = new AutoResetEvent(false);

                    _marketInformationPendingStateBuffer.Add(symbol, marketInformationPendingState);
                }

                _marketInformationPendingStateBuffer[symbol].Set();
            }
        }

        private void EvaluateMarketInformations(string symbol)
        {
            try
            {
                if (!CrosscheckMarketInformationComponents(symbol, out MarketDirection marketDirection))
                    return;

                if (!CreateMarketSignal(symbol, marketDirection, out MarketSignal marketSignal))
                    return;

                UpdateMarketSignalCollection(marketSignal);
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


        #region Create data model

        private bool CreateMarketEntity(string symbol, out MarketEntity marketEntity)
        {
            marketEntity = null;

            IEnumerable<BybitTrade> marketTrades = GetLatestMarketTradesEntry(symbol);
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

                _logger.Debug($"Created market entity window from {marketEntityWindow.Count()} market entities for symbol {symbol}.");
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
            {
                _logger.Warn($"Failed to create market information instance for symbol {symbol}.");
                return false;
            }

            if (!CreateMarketVolumeComponent(symbol, marketEntityWindow, out MarketVolumeComponent marketVolumeComponent))
            {
                _logger.Warn($"Failed to create market information instance for symbol {symbol}.");
                return false;
            }

            if (!CreateMarketPriceComponent(symbol, marketEntityWindow, out MarketPriceComponent marketPriceComponent))
            {
                _logger.Warn($"Failed to create market information instance for symbol {symbol}.");
                return false;
            }

            // both component should be pointing at the same thing. BUY volume -> price increases totally and partially on same levels or with delay???

            _logger.Debug("Created market information.");

            marketInformation = new MarketInformation(symbol);
            marketInformation.MarketVolumeComponent = marketVolumeComponent;
            marketInformation.MarketPriceComponent = marketPriceComponent;

            return true;
        }

        private bool CreateMarketVolumeComponent(string symbol, List<MarketEntity> marketEntityWindow, out MarketVolumeComponent marketVolumeComponent)
        {
            marketVolumeComponent = null;

            if (marketEntityWindow.IsNullOrEmpty())
            {
                _logger.Warn($"Failed to create market volume component for symbol {symbol}. Empty market entity window.");
                return false;
            }

            marketVolumeComponent = new MarketVolumeComponent(symbol, _volumeSetting);
            marketVolumeComponent.MarketEntityWindow = marketEntityWindow;

            return true;
        }

        private bool CreateMarketPriceComponent(string symbol, List<MarketEntity> marketEntityWindow, out MarketPriceComponent marketPriceComponent)
        {
            marketPriceComponent = null;

            if (marketEntityWindow.IsNullOrEmpty())
            {
                _logger.Warn($"Failed to create market price component for symbol {symbol}. Empty market entity window.");
                return false;
            }

            marketPriceComponent = new MarketPriceComponent(symbol, _priceChangeSetting);
            marketPriceComponent.MarketEntityWindow = marketEntityWindow;

            return true;
        }

        private bool CreateMarketSignal(string symbol, MarketDirection marketDirection, out MarketSignal marketSignal)
        {
            marketSignal = null;

            if (marketDirection == MarketDirection.Unknown)
                return false;

            ApplicationEvent?.Invoke(this, new MarketManagerEventArgs(EventType.Debug, $"!!! {symbol} MARKET SIGNAL {(marketDirection == MarketDirection.Uptrend ? "BUY" : "SELL")} !!!")); //xxx for now

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

        private void RemoveHistoricMarketEntityCollection(string symbol)
        {
            var historicMarketEntities = GetHistoricMarketEntityCollection(symbol);
            if (historicMarketEntities.IsNullOrEmpty())
                return;

            //_logger.Debug($"Removing {historicMarketEntities.Count()} historic market entities for symbol {symbol}.");

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

        private void RemoveHistoricMarketInformationCollection(string symbol)
        {
            var historicMarketInformations = GetHistoricMarketInformationCollection(symbol);
            if (historicMarketInformations.IsNullOrEmpty())
                return;

            //_logger.Debug($"Removing {historicMarketInformations.Count()} historic market informations for symbol {symbol}.");

            lock (_marketInformationCollection)
            {
                if (_marketInformationCollection.TryGetValue(symbol, out _))
                {
                    _marketEntityCollection[symbol].RemoveAll(x => historicMarketInformations.Equals(x.Id));
                }
            }
        }

        private void UpdateMarketTickerCollection(BybitSpotTickerUpdate ticker)
        {
            if (ticker == null)
            {
                return;
            }

            lock (_marketTickerBuffer)
            {
                bool hasUpdatedValue = false;

                if (!_marketTickerBuffer.TryGetValue(ticker.Symbol, out BybitSpotTickerUpdate t))
                {
                    _marketTickerBuffer.Add(ticker.Symbol, ticker);

                    hasUpdatedValue = true;
                }
                else if (t.LastPrice != ticker.LastPrice)
                {
                    _marketTickerBuffer[ticker.Symbol] = ticker;

                    hasUpdatedValue = true;
                }

                if (hasUpdatedValue)
                {
                    // price changed, update ticker buffer and dump relevant data to verbose
                    _verboseLogger.Debug($"{ticker.Symbol} NEW PRICE: {ticker.LastPrice}.");

                    MarketInformation latestMarketInformation = GetLatestMarketInformationEntry(ticker.Symbol);
                    if (latestMarketInformation != null)
                    {
                        _verboseLogger.Debug($"\n{latestMarketInformation}\n{latestMarketInformation?.MarketVolumeComponent}\n{latestMarketInformation?.MarketPriceComponent}\n");
                    }
                }
            }

        }

        private void UpdateMarketSignalCollection(MarketSignal marketSignal)
        {
            if (marketSignal == null)
                return;

            lock (_marketSignalCollection)
            {
                if (!_marketSignalCollection.TryGetValue(marketSignal.Symbol, out _))
                {
                    List<MarketSignal> marketSignals = new List<MarketSignal>() { marketSignal };
                    _marketSignalCollection.Add(marketSignal.Symbol, marketSignals);
                }
                else
                {
                    _marketSignalCollection[marketSignal.Symbol].Add(marketSignal);
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

                UpdateMarketTickerCollection(ticker.Data);
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
