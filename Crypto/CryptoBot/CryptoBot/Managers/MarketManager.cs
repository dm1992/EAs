using Bybit.Net.Clients;
using Bybit.Net.Enums;
using Bybit.Net.Objects;
using Bybit.Net.Objects.Models.Socket.Spot;
using Bybit.Net.Objects.Models.V5;
using CryptoBot.Models;
using CryptoBot.EventArgs;
using CryptoBot.Interfaces;
using CryptoBot.Interfaces.Managers;
using CryptoExchange.Net.Objects;
using CryptoExchange.Net.Sockets;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CryptoBot.Managers
{
    public class MarketManager : IMarketManager
    {
        private readonly ITradingManager _tradingManager;
        private readonly IOrderManager _orderManager;
        private readonly AppConfig _config;
        private readonly SemaphoreSlim _tradeSemaphore;
        private readonly BybitSocketClient _webSocket;

        private List<BybitTrade> _tradeBuffer;
        private List<string> _availableSymbols;
        private Dictionary<string, BybitOrderbook> _orderBooks;
        private bool _isInitialized;

        public event EventHandler<ApplicationEventArgs> ApplicationEvent;

        public MarketManager(ITradingManager tradingManager, IOrderManager orderManager, AppConfig config)
        {
            _tradingManager = tradingManager;
            _orderManager = orderManager;
            _config = config;
            _tradeSemaphore = new SemaphoreSlim(1, 1);

            BybitSocketClientOptions webSocketOptions = BybitSocketClientOptions.Default;
            webSocketOptions.V5StreamsOptions.OutputOriginalData = true;
            webSocketOptions.V5StreamsOptions.BaseAddress = _config.SpotStreamEndpoint;

            _webSocket = new BybitSocketClient(webSocketOptions);

            _tradeBuffer = new List<BybitTrade>();
            _orderBooks = new Dictionary<string, BybitOrderbook>();
            _isInitialized = false;
        }

        public bool Initialize()
        {
            try
            {
                if (_isInitialized) return true;

                var response = _tradingManager.GetAvailableSymbols();
                response.Wait();

                _availableSymbols = response.Result.ToList();

                if (_availableSymbols.IsNullOrEmpty())
                {
                    throw new Exception("No available symbols.");
                }

                ApplicationEvent?.Invoke(this, new MarketManagerEventArgs(EventType.Information, "Initialized."));

                _isInitialized = true;
                return true;
            }
            catch (Exception e)
            {
                ApplicationEvent?.Invoke(this, new MarketManagerEventArgs(EventType.Error, $"!!!Initialization failed!!! {e}"));
                return false;
            }
        }

        public async void InvokeWebSocketEventSubscription()
        {
            if (!_isInitialized) return;

            ApplicationEvent?.Invoke(this, new MarketManagerEventArgs(EventType.Information, "Invoking web socket event subscription."));

            CallResult<UpdateSubscription> updateSubscription = await _webSocket.V5SpotStreams.SubscribeToTradeUpdatesAsync(_availableSymbols, HandleTrades);
            updateSubscription.Data.ConnectionRestored += WebSocketEventSubscription_TradeUpdatesConnectionRestored;
            updateSubscription.Data.ConnectionLost += WebSocketEventSubscription_TradeUpdatesConnectionLost;
            updateSubscription.Data.ConnectionClosed += WebSocketEventSubscription_TradeUpdatesConnectionClosed;

            updateSubscription = await _webSocket.V5SpotStreams.SubscribeToOrderbookUpdatesAsync(_availableSymbols, 50, HandleOrderbookSnapshot, HandleOrderbookUpdate);
            updateSubscription.Data.ConnectionRestored += WebSocketEventSubscription_OrderbookUpdatesConnectionRestored;
            updateSubscription.Data.ConnectionLost += WebSocketEventSubscription_OrderbookUpdatesConnectionLost;
            updateSubscription.Data.ConnectionClosed += WebSocketEventSubscription_OrderbookUpdatesConnectionClosed;
        }

        public async void CloseWebSocketEventSubscription()
        {
            ApplicationEvent?.Invoke(this, new MarketManagerEventArgs(EventType.Information, "Closing web socket event subscription."));

            await _webSocket.V5SpotStreams.UnsubscribeAllAsync();
        }

        private bool DetermineMarketVolumeIntensity(string symbol, MarketSpectatorMode marketSpectatorMode, out MarketVolumeIntensity marketVolumeIntensity)
        {
            marketVolumeIntensity = MarketVolumeIntensity.Unknown;

            if (!GetMarketLevelDepth(marketSpectatorMode, out int? marketLevelDepth))
                return false;

            return marketVolumeIntensity != MarketVolumeIntensity.Unknown;
        }

        private bool DetermineMarketDirection(string symbol, MarketSpectatorMode marketSpectatorMode, out MarketDirection marketDirection)
        {
            marketDirection = MarketDirection.Unknown;

            if (!GetMarketLevelDepth(marketSpectatorMode, out int? marketLevelDepth))
                return false;

            return marketDirection != MarketDirection.Unknown;
        }

        /// <summary>
        /// Get market level depth regarding executed trades or elapsed time.
        /// </summary>
        /// <param name="marketSpectatorMode"></param>
        /// <param name="marketLevelDepth"></param>
        /// <returns></returns>
        private bool GetMarketLevelDepth(MarketSpectatorMode marketSpectatorMode, out int? marketLevelDepth)
        {
            marketLevelDepth = null;

            switch (marketSpectatorMode)
            {
                case MarketSpectatorMode.ExecutedTrades_MicroLevel:
                    marketLevelDepth = _config.ElapsedTimeMicroLevel;
                    break;

                case MarketSpectatorMode.ExecutedTrades_MacroLevel:
                    marketLevelDepth = _config.ElapsedTimeMacroLevel;
                    break;

                case MarketSpectatorMode.ElapsedTime_MicroLevel:
                    marketLevelDepth = _config.ElapsedTimeMicroLevel;
                    break;

                case MarketSpectatorMode.ElapsedTime_MacroLevel:
                    marketLevelDepth = _config.ElapsedTimeMacroLevel;
                    break;

                default:
                    ApplicationEvent?.Invoke(this, new MarketManagerEventArgs(EventType.Error, $"!!!Failed to get market level depth. Not supported market spectator mode {marketSpectatorMode}!!!"));
                    break;
            }

            return marketLevelDepth.HasValue;
        }

        /// <summary>
        ///  Prepare symbol market metrics like volume and direction according to number of executed trades or elapsed time. 
        /// </summary>
        /// <param name="symbol"></param>
        private void PrepareMarketMetric(string symbol)
        {
            //xxx for now market spectator set to micro level
            DetermineMarketDirection(symbol, MarketSpectatorMode.ExecutedTrades_MicroLevel, out MarketDirection marketDirectionExecutedTrades);
            DetermineMarketDirection(symbol, MarketSpectatorMode.ElapsedTime_MicroLevel, out MarketDirection marketDirectionElapsedTime);
            DetermineMarketVolumeIntensity(symbol, MarketSpectatorMode.ExecutedTrades_MicroLevel, out MarketVolumeIntensity marketVolumeIntensityExecutedTrades);
            DetermineMarketVolumeIntensity(symbol, MarketSpectatorMode.ElapsedTime_MicroLevel, out MarketVolumeIntensity marketVolumeIntensityElapsedTime);

            MarketMetric marketMetric = new MarketMetric(symbol);
            marketMetric.MarketDirection_ExecutedTrades = marketDirectionExecutedTrades;
            marketMetric.MarketDirection_ElapsedTime = marketDirectionElapsedTime;
            marketMetric.MarketVolumeIntensity_ExecutedTrades = marketVolumeIntensityExecutedTrades;
            marketMetric.MarketVolumeIntensity_ElapsedTime = marketVolumeIntensityElapsedTime;

            HandleMarketMetric(marketMetric);
        }

        private void HandleMarketMetric(MarketMetric marketMetric)
        {
            if (marketMetric == null) return;

            //ApplicationEvent?.Invoke(this, new MarketManagerEventArgs(EventType.Debug, $"{marketMetric.Dump()}"));

            if (marketMetric.ValidateMarketDirection(out MarketDirection marketDirection))
            {
                if (marketMetric.ValidateMarketVolumeIntensity(out MarketVolumeIntensity marketVolumeIntensity))
                {
                    if (marketDirection == MarketDirection.Uptrend && marketVolumeIntensity == MarketVolumeIntensity.BigBuyers)
                    {
                        _orderManager.InvokeOrder(marketMetric.Symbol, OrderSide.Buy);
                    }
                    else if (marketDirection == MarketDirection.Downtrend && marketVolumeIntensity == MarketVolumeIntensity.BigSellers)
                    {
                        _orderManager.InvokeOrder(marketMetric.Symbol, OrderSide.Sell);
                    }
                }
            }

        }

        #region Event handlers

        private void WebSocketEventSubscription_TradeUpdatesConnectionRestored(TimeSpan obj)
        {
            ApplicationEvent?.Invoke(this, new MarketManagerEventArgs(EventType.Information, "Subscription to trade updates restored."));
        }

        private void WebSocketEventSubscription_TradeUpdatesConnectionLost()
        {
            ApplicationEvent?.Invoke(this, new MarketManagerEventArgs(EventType.Warning, "Subscription to trade updates lost."));
        }

        private void WebSocketEventSubscription_TradeUpdatesConnectionClosed()
        {
            ApplicationEvent?.Invoke(this, new MarketManagerEventArgs(EventType.Information, "Subscription to trade updates closed."));
        }

        private void WebSocketEventSubscription_OrderbookUpdatesConnectionRestored(TimeSpan obj)
        {
            ApplicationEvent?.Invoke(this, new MarketManagerEventArgs(EventType.Information, "Subscription to orderbook updates restored."));
        }

        private void WebSocketEventSubscription_OrderbookUpdatesConnectionLost()
        {
            ApplicationEvent?.Invoke(this, new MarketManagerEventArgs(EventType.Information, "Subscription to orderbook updates lost."));
        }

        private void WebSocketEventSubscription_OrderbookUpdatesConnectionClosed()
        {
            ApplicationEvent?.Invoke(this, new MarketManagerEventArgs(EventType.Information, "Subscription to orderbook updates closed."));
        }

        private void HandleTrades(DataEvent<IEnumerable<BybitTrade>> trades)
        {
            try
            {
                _tradeSemaphore.WaitAsync();

                _tradeBuffer.Concat(trades.Data);

                PrepareMarketMetric(trades.Topic);
            }
            catch (Exception e)
            {
                ApplicationEvent?.Invoke(this, new MarketManagerEventArgs(EventType.Error, $"!!!HandleTrades failed!!! {e}"));
            }
            finally
            {
                _tradeSemaphore.Release();
            }
        }

        private void HandleOrderbookSnapshot(DataEvent<BybitOrderbook> orderbook)
        {
            UpdateOrderbookEntry(orderbook.Data);
        }

        private void HandleOrderbookUpdate(DataEvent<BybitOrderbook> orderbook)
        {
            UpdateOrderbookEntry(orderbook.Data);
        }

        private void UpdateOrderbookEntry(BybitOrderbook orderbook)
        {
            if (orderbook == null) return;

            lock (_orderBooks)
            {
                if (!_orderBooks.TryGetValue(orderbook.Symbol, out _))
                {
                    _orderBooks.Add(orderbook.Symbol, orderbook);
                }
                else
                {
                    _orderBooks[orderbook.Symbol] = orderbook;
                }
            }
        }

        #endregion
    }
}
