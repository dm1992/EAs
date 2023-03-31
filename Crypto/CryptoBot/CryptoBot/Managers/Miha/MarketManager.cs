using Bybit.Net.Clients;
using Bybit.Net.Objects;
using Bybit.Net.Objects.Models.Socket.Spot;
using Bybit.Net.Objects.Models.Spot.v3;
using CryptoBot.Data;
using CryptoBot.EventArgs;
using CryptoBot.Interfaces;
using CryptoExchange.Net.Sockets;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using static CryptoBot.Data.Candle;

namespace CryptoBot.Managers.Miha
{
    public class MarketManager : IMarketManager
    {
        private readonly Config _config;
        private readonly BybitClient _bybitClient;
        private readonly SemaphoreSlim _tradeSemaphore;

        private List<DataEvent<BybitSpotTradeUpdate>> _tradeBuffer;
        private List<CandleBatch> _candleBatches;
        private List<string> _availableMarketSymbols;
        private List<UpdateSubscription> _subscriptions;

        public event EventHandler<ApplicationEventArgs> ApplicationEvent;

        public bool IsInitialized { get; private set; } = false;

        public MarketManager(Config config)
        {
            _config = config;
            _tradeSemaphore = new SemaphoreSlim(1, 1);

            _tradeBuffer = new List<DataEvent<BybitSpotTradeUpdate>>();
            _candleBatches = new List<CandleBatch>();
            _subscriptions = new List<UpdateSubscription>();

            BybitClientOptions clientOptions = BybitClientOptions.Default;
            clientOptions.SpotApiOptions.AutoTimestamp = true;
            clientOptions.SpotApiOptions.BaseAddress = _config.ApiEndpoint;
            _bybitClient = new BybitClient(clientOptions);
        }

        public void Initialize()
        {
            if (IsInitialized) return;

            _availableMarketSymbols = GetAvailableMarketSymbols();
            if (_availableMarketSymbols == null)
            {
                ApplicationEvent?.Invoke(this, new ApplicationEventArgs(EventType.Warning,
                message: $"Failed to get available market symbols."));

                return;
            }

            SetCandleBatches();

            Task.Run(() => { MonitorCandleBatches(); });

            ApplicationEvent?.Invoke(this, new ApplicationEventArgs(EventType.Information,
            message: $"Initialized market manager."));

            this.IsInitialized = true;
        }

        private List<string> GetAvailableMarketSymbols()
        {
            if (!_config.Symbols.IsNullOrEmpty())
                return _config.Symbols.ToList();

            var response = _bybitClient.SpotApiV3.ExchangeData.GetSymbolsAsync();
            response.Wait();

            var symbols = response.Result?.Data;
            if (symbols.IsNullOrEmpty())
                return new List<string>();

            return symbols.Select(x => x.Name).ToList();
        }

        private void SetCandleBatches()
        {
            lock (_candleBatches)
            {
                _candleBatches.Clear();

                foreach (var symbol in _availableMarketSymbols)
                {
                    CandleBatch candleBatch = new CandleBatch(symbol);

                    _candleBatches.Add(candleBatch);
                }
            }
        }

        private void MonitorCandleBatches()
        {
            try
            {
                ApplicationEvent?.Invoke(this, new ApplicationEventArgs(EventType.Information,
                message: $"Monitoring candle batch(es) in progress..."));

                while (true)
                {
                    lock (_candleBatches)
                    {
                        foreach (var symbol in _availableMarketSymbols)
                        {
                            CandleBatch candleBatch = _candleBatches.FirstOrDefault(x => x.Symbol == symbol);
                            if (candleBatch == null)
                            {
                                ApplicationEvent?.Invoke(this, new ApplicationEventArgs(EventType.Error,
                                message: $"!!!Unable to find candle batch for symbol {symbol}."));

                                continue;
                            }

                            // find first active candle inside active candle batch and check it
                            Candle activeCandle = candleBatch.Candles.FirstOrDefault(x => !x.Completed);
                            if (activeCandle != null)
                            {
                                if ((DateTime.Now - activeCandle.CreatedAt).TotalMinutes >= _config.CandleMinuteTimeframe)
                                {
                                    activeCandle.Completed = true;

                                    ApplicationEvent?.Invoke(this, 
                                    new ApplicationEventArgs(EventType.Information,
                                    message: $"{activeCandle.Dump()}\n\n{activeCandle.DumpTrades()}", 
                                    messageScope: $"verbose_{activeCandle.Symbol}"));
                                }
                            }
                        }

                        bool candleBatchesCompleted = _candleBatches.All(x => x.Completed && x.Candles.Count == _config.CandlesInBatch);

                        if (candleBatchesCompleted)
                        {
                            ApplicationEvent?.Invoke(this, new ApplicationEventArgs(EventType.Information,
                            message: $"Completed all symbol candle batch(es). Will start over again..."));

                            DumpCandleBatches();

                            SetCandleBatches(); // reset candle batches and start over again
                        }
                    }
                }
            }
            catch (Exception e)
            {
                ApplicationEvent?.Invoke(this, new ApplicationEventArgs(EventType.Error,
                message: $"!!!MonitorCandleBatches failed!!! {e}"));

                DumpCandleBatches();
            }
        }

        private void DumpCandleBatches()
        {
            foreach (var candleBatch in _candleBatches)
            {
                ApplicationEvent?.Invoke(this, new ApplicationEventArgs(EventType.Information,
                message: $"{candleBatch.Dump()}"));

                ApplicationEvent?.Invoke(this, new ApplicationEventArgs(EventType.Information,
                message: $"{candleBatch.Dump(generalInfo: false)}",
                messageScope: $"candleBatch_{candleBatch.Symbol}"));
            }
        }

        private void AddTradeToCandleBatch(DataEvent<BybitSpotTradeUpdate> trade)
        {
            if (trade == null) return;

            try
            {
                lock (_candleBatches)
                {
                    CandleBatch candleBatch = _candleBatches.FirstOrDefault(x => x.Symbol == trade.Topic);
                    if (candleBatch == null)
                    {
                        ApplicationEvent?.Invoke(this, new ApplicationEventArgs(EventType.Error,
                        message: $"!!!Unable to find candle batch for symbol {trade.Topic}."));

                        return;
                    }

                    if (candleBatch.Candles.Count == _config.CandlesInBatch)
                    {
                        if (candleBatch.Completed)
                        {
                            // candle batch for this symbol is completed, exit
                            return;
                        }
                    }

                    Candle activeCandle = candleBatch.Candles.FirstOrDefault(x => !x.Completed);
                    if (activeCandle == null)
                    {
                        activeCandle = new Candle(trade.Topic);
                        candleBatch.Candles.Add(activeCandle);
                    }

                    activeCandle.TradeBuffer.Add(trade);
                }
            }
            catch (Exception e)
            {
                ApplicationEvent?.Invoke(this, new ApplicationEventArgs(EventType.Error,
                message: $"!!!AddTradeToCandleBatch failed!!! {e}"));
            }
        }

        private void CheckTradeForPriceClosure(DataEvent<BybitSpotTradeUpdate> trade)
        {
            if (trade == null) return;

            try
            {
                bool closePrice = false;

                var symbolTradePrices = new List<decimal>();
                var symbolTrades = _tradeBuffer.Where(x => x.Topic == trade.Topic).OrderBy(x => x.Data.Timestamp); // ordered trades
                
                foreach (var symbolTrade in symbolTrades)
                {
                    if (!symbolTradePrices.Contains(symbolTrade.Data.Price))
                    {
                        symbolTradePrices.Add(symbolTrade.Data.Price);
                    }

                    if (symbolTradePrices.Count() >= _config.PriceLevelChanges)
                    {
                        closePrice = true;
                        break;
                    }
                }

                if (closePrice)
                {
                    decimal symbolLatestPrice = symbolTrades.Last().Data.Price;
                    decimal symbolClosePrice = symbolTradePrices.First();
                    List<DataEvent<BybitSpotTradeUpdate>> symbolClosePriceTrades = symbolTrades.Where(x => x.Data.Price == symbolClosePrice).ToList();

                    PriceClosure priceClosure = new PriceClosure(trade.Topic, symbolLatestPrice, symbolClosePriceTrades);

                    ApplicationEvent?.Invoke(this, new ApplicationEventArgs(EventType.Information,
                    message: $"{priceClosure.Dump()}",
                    messageScope: $"priceClosure_{trade.Topic}"));

                    _tradeBuffer.RemoveAll(x => x.Topic == trade.Topic && x.Data.Price == symbolClosePrice);
                }
            }
            catch (Exception e)
            {
                ApplicationEvent?.Invoke(this, new ApplicationEventArgs(EventType.Error,
                message: $"!!!CheckTradeForPriceClosure failed!!! {e}"));
            }
        }

        public bool GetCurrentMarket(string symbol, out IMarket market)
        {
            throw new NotImplementedException();
        }

        public void InvokeAPISubscription()
        {
            ApplicationEvent?.Invoke(this, new ApplicationEventArgs(EventType.Information,
            message: $"Invoked API subscription in market manager."));

            BybitSocketClientOptions socketClientOptions = BybitSocketClientOptions.Default;
            socketClientOptions.SpotStreamsV3Options.OutputOriginalData = true;
            socketClientOptions.SpotStreamsV3Options.BaseAddress = _config.SpotStreamEndpoint;

            BybitSocketClient socketClient = new BybitSocketClient(socketClientOptions);

            foreach (var symbol in _availableMarketSymbols)
            {
                _subscriptions.Add(socketClient.SpotStreamsV3.SubscribeToTradeUpdatesAsync(symbol, HandleTrade).GetAwaiter().GetResult().Data); // deadlock issue, async method in sync manner
            }

            foreach (var subscription in _subscriptions)
            {
                subscription.ConnectionRestored += API_Subscription_ConnectionRestored;
                subscription.ConnectionLost += API_Subscription_ConnectionLost;
                subscription.ConnectionClosed += API_Subscription_ConnectionClosed;
            }
        }

        public async void CloseAPISubscription()
        {
            foreach (var subscription in _subscriptions)
            {
                await subscription.CloseAsync();
            }
        }

        #region Event handlers

        private void API_Subscription_ConnectionRestored(TimeSpan obj)
        {
            ApplicationEvent?.Invoke(this, new ApplicationEventArgs(EventType.Information,
            message: $"API subscription connection restored in market manager."));
        }

        private void API_Subscription_ConnectionLost()
        {
            ApplicationEvent?.Invoke(this, new ApplicationEventArgs(EventType.Information,
            message: $"API subscription connection lost in market manager."));
        }

        private void API_Subscription_ConnectionClosed()
        {
            ApplicationEvent?.Invoke(this, new ApplicationEventArgs(EventType.Information,
            message: $"API subscription connection closed in market manager."));
        }

        private void HandleTrade(DataEvent<BybitSpotTradeUpdate> trade)
        {
            try
            {
                _tradeSemaphore.WaitAsync();

                trade.Topic = (string)Extensions.ParseObject(trade.OriginalData, "topic");
                if (trade.Topic == null)
                {
                    ApplicationEvent?.Invoke(this, new ApplicationEventArgs(EventType.Error,
                    message: $"!!!Unable to parse topic from original trade data."));

                    return;
                }

                _tradeBuffer.Add(trade);

                AddTradeToCandleBatch(trade);

                CheckTradeForPriceClosure(trade);
            }
            catch (Exception e)
            {
                ApplicationEvent?.Invoke(this, new ApplicationEventArgs(EventType.Error,
                message: $"!!!HandleTrade failed!!! {e}"));
            }
            finally
            {
                _tradeSemaphore.Release();
            }
        }

        #endregion
    }
}
