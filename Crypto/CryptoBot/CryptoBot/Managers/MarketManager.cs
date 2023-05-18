using Bybit.Net.Clients;
using Bybit.Net.Enums;
using Bybit.Net.Objects;
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
        private readonly SemaphoreSlim _tickerSemaphore;
        private readonly SemaphoreSlim _tradeSemaphore;
        private readonly SemaphoreSlim _orderbookSnapshotSemaphore;
        private readonly SemaphoreSlim _orderbookUpdateSemaphore;
        private readonly BybitSocketClient _webSocket;

        private List<BybitTrade> _trades;
        private List<TradingSignal> _tradingSignals;
        private List<string> _availableSymbols;
        private Dictionary<string, BybitOrderbook> _orderbooks;
        private Dictionary<string, BybitSpotTickerUpdate> _tickers;
        private bool _isInitialized;

        private MarketDirection _marketDirection = MarketDirection.Unknown;
        private object _marketDirectionLocker = new object();
        

        public event EventHandler<ApplicationEventArgs> ApplicationEvent;

        public MarketManager(ITradingManager tradingManager, IOrderManager orderManager, AppConfig config)
        {
            _tradingManager = tradingManager;
            _orderManager = orderManager;
            _config = config;
            _tickerSemaphore = new SemaphoreSlim(1, 1);
            _tradeSemaphore = new SemaphoreSlim(1, 1);
            _orderbookSnapshotSemaphore = new SemaphoreSlim(1, 1);
            _orderbookUpdateSemaphore = new SemaphoreSlim(1, 1);

            BybitSocketClientOptions webSocketOptions = BybitSocketClientOptions.Default;
            webSocketOptions.V5StreamsOptions.OutputOriginalData = true;
            webSocketOptions.V5StreamsOptions.BaseAddress = _config.SpotStreamEndpoint;

            _webSocket = new BybitSocketClient(webSocketOptions);

            _trades = new List<BybitTrade>();
            _tradingSignals = new List<TradingSignal>();
            _orderbooks = new Dictionary<string, BybitOrderbook>();
            _tickers = new Dictionary<string, BybitSpotTickerUpdate>();
            _isInitialized = false;

            Task.Run(() => MonitorActiveTradingSignals());
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

            CallResult<UpdateSubscription> response = await _webSocket.V5SpotStreams.SubscribeToTradeUpdatesAsync(_availableSymbols, HandleTrades);
            if (!response.GetResultOrError(out UpdateSubscription updateSubscription, out Error error))
            {
                throw new Exception($"Failed to subscribe to trade updates. Error: ({error?.Code}) {error?.Message}.");
            }

            updateSubscription.ConnectionRestored += WebSocketEventSubscription_TradeUpdatesConnectionRestored;
            updateSubscription.ConnectionLost += WebSocketEventSubscription_TradeUpdatesConnectionLost;
            updateSubscription.ConnectionClosed += WebSocketEventSubscription_TradeUpdatesConnectionClosed;

            response = await _webSocket.V5SpotStreams.SubscribeToOrderbookUpdatesAsync(_availableSymbols, 50, HandleOrderbookSnapshot, HandleOrderbookUpdate);
            if (!response.GetResultOrError(out updateSubscription, out error))
            {
                throw new Exception($"Failed to subscribe to orderbook updates. Error: ({error?.Code}) {error?.Message}.");
            }

            updateSubscription.ConnectionRestored += WebSocketEventSubscription_OrderbookUpdatesConnectionRestored;
            updateSubscription.ConnectionLost += WebSocketEventSubscription_OrderbookUpdatesConnectionLost;
            updateSubscription.ConnectionClosed += WebSocketEventSubscription_OrderbookUpdatesConnectionClosed;

            response = await _webSocket.V5SpotStreams.SubscribeToTickerUpdatesAsync(_availableSymbols, HandleTicker);
            if (!response.GetResultOrError(out updateSubscription, out error))
            {
                throw new Exception($"Failed to subscribe to ticker updates. Error: ({error?.Code}) {error?.Message}.");
            }

            updateSubscription.ConnectionRestored += WebSocketEventSubscription_TickerUpdatesConnectionRestored;
            updateSubscription.ConnectionLost += WebSocketEventSubscription_TickerUpdatesConnectionLost;
            updateSubscription.ConnectionClosed += WebSocketEventSubscription_TickerUpdatesConnectionClosed;
        }

        public async void CloseWebSocketEventSubscription()
        {
            ApplicationEvent?.Invoke(this, new MarketManagerEventArgs(EventType.Information, "Closing web socket event subscription."));

            await _webSocket.V5SpotStreams.UnsubscribeAllAsync();
        }

        private MarketDirection GetLatestMarketDirection()
        {
            lock (_marketDirectionLocker)
            {
                return _marketDirection;
            }
        }

        private BybitOrderbook GetLatestOrderbookEntry(string symbol)
        {
            lock (_orderbooks)
            {
                if (!_orderbooks.TryGetValue(symbol, out BybitOrderbook orderbook))
                {
                    return null;
                }

                return orderbook;
            }
        }

        private BybitSpotTickerUpdate GetLatestTickerEntry(string symbol)
        {
            lock (_tickers)
            {
                if (!_tickers.TryGetValue(symbol, out BybitSpotTickerUpdate ticker))
                {
                    return null;
                }

                return ticker;
            }
        }

        private void UpdateOrderbookEntry(BybitOrderbook orderbook)
        {
            if (orderbook == null) return;

            lock (_orderbooks)
            {
                if (!_orderbooks.TryGetValue(orderbook.Symbol, out BybitOrderbook orderbookEntry))
                {
                    _orderbooks.Add(orderbook.Symbol, orderbook);
                }
                else
                {
                    if (!orderbook.Bids.IsNullOrEmpty())
                    {
                        List<BybitOrderbookEntry> bids = orderbookEntry.Bids.ToList();

                        for (int i = 0; i < orderbook.Bids.Count(); i++)
                        {
                            bids[i] = orderbook.Bids.ElementAt(i);
                        }

                        orderbookEntry.Bids = bids;
                    }

                    if (!orderbook.Asks.IsNullOrEmpty())
                    {
                        List<BybitOrderbookEntry> asks = orderbookEntry.Asks.ToList();

                        for (int i = 0; i < orderbook.Asks.Count(); i++)
                        {
                            asks[i] = orderbook.Asks.ElementAt(i);
                        }

                        orderbookEntry.Asks = asks;
                    }
                }
            }
        }

        private void UpdateTickerEntry(BybitSpotTickerUpdate ticker)
        {
            if (ticker == null) return;

            lock (_tickers)
            {
                if (!_tickers.TryGetValue(ticker.Symbol, out _))
                {
                    _tickers.Add(ticker.Symbol, ticker);
                }
                else
                {
                    _tickers[ticker.Symbol] = ticker;
                }
            }
        }

        private void UpdateMarketDirection(BybitSpotTickerUpdate ticker)
        {
            if (ticker == null) return;

            decimal averagePrice24h = (ticker.HighPrice24h + ticker.LowPrice24h) / 2.0m;

            if (ticker.LastPrice > averagePrice24h)
            {
                if (ticker.PricePercentage24h > 0)
                {
                    _marketDirection = MarketDirection.Uptrend;
                }
            }
            else if (ticker.LastPrice < averagePrice24h)
            {
                if (ticker.PricePercentage24h < 0)
                {
                    _marketDirection = MarketDirection.Downtrend;
                }
            }
        }

        private void MonitorActiveTradingSignals()
        {
            ApplicationEvent?.Invoke(this, new MarketManagerEventArgs(EventType.Debug, $"MonitorActiveTradingSignals started."));

            while (true)
            {
                foreach (var tradingSignalSymbolGroup in _tradingSignals.Where(x => x.Active).GroupBy(x => x.Symbol))
                {
                    foreach (var tradingSignal in tradingSignalSymbolGroup)
                    {
                        // handle trading signal depending on current market situation.
                    }
                }

                Thread.Sleep(10);
            }
        }

        
        #region Subscription handlers

        private async void HandleTicker(DataEvent<BybitSpotTickerUpdate> ticker)
        {
            try
            {
                await _tickerSemaphore.WaitAsync();

                UpdateTickerEntry(ticker.Data);

                UpdateMarketDirection(ticker.Data);
            }
            catch (Exception e)
            {
                ApplicationEvent?.Invoke(this, new OrderManagerEventArgs(EventType.Error, $"HandleTicker failed. {e}"));
            }
            finally
            {
                _tickerSemaphore.Release();
            }
        }

        private void HandleTrades(DataEvent<IEnumerable<BybitTrade>> trades)
        {
            try
            {
                _tradeSemaphore.WaitAsync();

                _trades.AddRange(trades.Data);

            }
            catch (Exception e)
            {
                ApplicationEvent?.Invoke(this, new MarketManagerEventArgs(EventType.Error, $"HandleTrades failed. {e}"));
            }
            finally
            {
                _tradeSemaphore.Release();
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
                ApplicationEvent?.Invoke(this, new MarketManagerEventArgs(EventType.Error, $"HandleOrderbookSnapshot failed. {e}"));
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
                ApplicationEvent?.Invoke(this, new MarketManagerEventArgs(EventType.Error, $"HandleOrderbookUpdate failed. {e}"));
            }
            finally
            {
                _orderbookUpdateSemaphore.Release();
            }
        }

        #endregion


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

        private void WebSocketEventSubscription_TickerUpdatesConnectionRestored(TimeSpan obj)
        {
            ApplicationEvent?.Invoke(this, new OrderManagerEventArgs(EventType.Information, "Subscription to ticker updates restored."));
        }

        private void WebSocketEventSubscription_TickerUpdatesConnectionLost()
        {
            ApplicationEvent?.Invoke(this, new OrderManagerEventArgs(EventType.Warning, "Subscription to ticker updates lost."));
        }

        private void WebSocketEventSubscription_TickerUpdatesConnectionClosed()
        {
            ApplicationEvent?.Invoke(this, new OrderManagerEventArgs(EventType.Information, "Subscription to ticker updates closed."));
        }

        #endregion
    }
}
