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
                $"Failed to get available market symbols."));

                return;
            }

            SetupCandleBatches();

            Task.Run(() => { MonitorCandleBatches(); });

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

        private void SetupCandleBatches()
        {
            ApplicationEvent?.Invoke(this, new ApplicationEventArgs(EventType.Information,
            $"Setup candle batch(es) for {_availableMarketSymbols.Count} symbol(s) and tracking {_config.CandlesInBatch} candle(s) per batch within {_config.CandleMinuteTimeframe} minute candle timeframe."));

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
                $"Monitoring candle batch(es) in progress..."));

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
                                $"!!!Unable to find candle batch for symbol {symbol}."));

                                continue;
                            }

                            // find first active candle inside active candle batch and check it
                            Candle activeCandle = candleBatch.Candles.FirstOrDefault(x => !x.Completed);
                            if (activeCandle != null)
                            {
                                if ((DateTime.Now - activeCandle.CreatedAt).TotalMinutes >= _config.CandleMinuteTimeframe)
                                {
                                    ApplicationEvent?.Invoke(this, new ApplicationEventArgs(EventType.Information,
                                    $"({activeCandle.Id}) {activeCandle.Symbol} candle completed. Total {activeCandle.TradeBuffer.Count} trades in candle.\n {activeCandle.Dump()}", activeCandle.Symbol));

                                    // dump collected trades in candle to verbose
                                    ApplicationEvent?.Invoke(this, new ApplicationEventArgs(EventType.Information,
                                    $"{activeCandle.DumpTrades()}", activeCandle.Symbol, verbose: true));

                                    activeCandle.Completed = true;
                                }
                            }
                        }

                        if (_candleBatches.All(x => x.Completed && x.Candles.Count == _config.CandlesInBatch))
                        {
                            ApplicationEvent?.Invoke(this, new ApplicationEventArgs(EventType.Information,
                            $"Completed all symbol candle batch(es). Will start over again..."));

                            DumpCandleBatches();

                            SetupCandleBatches(); // reset candle batches and start over again
                        }
                    }
                }
            }
            catch (Exception e)
            {
                ApplicationEvent?.Invoke(this, new ApplicationEventArgs(EventType.Error,
                $"!!!MonitorCandleBatches failed!!! {e}"));

                DumpCandleBatches();
            }
        }

        private void DumpCandleBatches()
        {
            foreach (var candleBatch in _candleBatches)
            {
                ApplicationEvent?.Invoke(this, new ApplicationEventArgs(EventType.Information,
                $"{candleBatch.Symbol} candle batch (completed: {candleBatch.Completed}). Total trades in candle batch: {candleBatch.Candles.Sum(x => x.TradeBuffer.Count)}.\n {candleBatch.Dump()}", candleBatch.Symbol));
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
                        $"!!!Unable to find candle batch for symbol {trade.Topic}."));

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
                $"!!!AddTradeToCandleBatch failed!!! {e}"));
            }
        }

        private void CheckTradeForPriceClosure(DataEvent<BybitSpotTradeUpdate> trade)
        {
            if (trade == null) return;

            try
            {
                var symbolTrades = _tradeBuffer.Where(x => x.Topic == trade.Topic).OrderBy(x => x.Data.Timestamp); // ordered trades
                var symbolPrices = new List<decimal>();
                bool closePriceLevel = false;

                foreach (var symbolTrade in symbolTrades)
                {
                    if (!symbolPrices.Contains(symbolTrade.Data.Price))
                    {
                        symbolPrices.Add(symbolTrade.Data.Price);
                    }

                    if (symbolPrices.Count() >= _config.MaxPriceLevelChanges)
                    {
                        closePriceLevel = true;
                        break;
                    }
                }

                if (closePriceLevel)
                {
                    decimal priceLevel = symbolPrices.First();

                    PriceLevelClosure priceLevelClosure = new PriceLevelClosure(trade.Topic, symbolTrades.Where(x => x.Data.Price == priceLevel).ToList(), symbolTrades.Last().Data.Price);

                    ApplicationEvent?.Invoke(this, new ApplicationEventArgs(EventType.Information,
                    $"{priceLevelClosure.Dump()}", trade.Topic));

                    _tradeBuffer.RemoveAll(x => x.Topic == trade.Topic && x.Data.Price == priceLevel);
                }
            }
            catch (Exception e)
            {
                ApplicationEvent?.Invoke(this, new ApplicationEventArgs(EventType.Error,
                $"!!!CheckTradeForPriceClosure failed!!! {e}"));
            }
        }

        public bool GetCurrentMarket(string symbol, out IMarket market)
        {
            throw new NotImplementedException();
        }

        public void InvokeAPISubscription()
        {
            ApplicationEvent?.Invoke(this, new ApplicationEventArgs(EventType.Information,
            $"Invoked API subscription in market manager."));

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
            $"API subscription connection restored in market manager."));
        }

        private void API_Subscription_ConnectionLost()
        {
            ApplicationEvent?.Invoke(this, new ApplicationEventArgs(EventType.Information,
            $"API subscription connection lost in market manager."));
        }

        private void API_Subscription_ConnectionClosed()
        {
            ApplicationEvent?.Invoke(this, new ApplicationEventArgs(EventType.Information,
            $"API subscription connection closed in market manager."));
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
                    $"!!!Unable to parse symbol from original trade data."));

                    return;
                }

                _tradeBuffer.Add(trade);

                AddTradeToCandleBatch(trade);

                CheckTradeForPriceClosure(trade);
            }
            catch (Exception e)
            {
                ApplicationEvent?.Invoke(this, new ApplicationEventArgs(EventType.Error,
                $"!!!HandleTrade failed!!! {e}"));
            }
            finally
            {
                _tradeSemaphore.Release();
            }
        }

        #endregion
    }
}
