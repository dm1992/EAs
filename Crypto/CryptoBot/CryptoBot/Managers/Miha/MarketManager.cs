using Bybit.Net.Clients;
using Bybit.Net.Objects;
using Bybit.Net.Objects.Models.Socket.Spot;
using Bybit.Net.Objects.Models.Spot.v3;
using CryptoBot.Data;
using CryptoBot.Data.Miha;
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
        private readonly ITradingAPIManager _tradingAPIManager;
        private readonly Config _config;
        private readonly SemaphoreSlim _tradeSemaphore;

        private List<DataEvent<BybitSpotTradeUpdate>> _tradeBuffer;
        private List<CandleBatch> _currentCandleBatches;
        private List<CandleBatch> _latestCandleBatches;
        private List<PriceClosure> _priceClosures;
        private List<string> _availableSymbols;
        private List<UpdateSubscription> _subscriptions;
        private bool _isInitialized;

        public event EventHandler<ApplicationEventArgs> ApplicationEvent;

        public MarketManager(ITradingAPIManager tradingAPIManager, Config config)
        {
            _tradingAPIManager = tradingAPIManager;
            _config = config;
            _tradeSemaphore = new SemaphoreSlim(1, 1);

            _tradeBuffer = new List<DataEvent<BybitSpotTradeUpdate>>();
            _priceClosures = new List<PriceClosure>();
            _currentCandleBatches = new List<CandleBatch>();
            _latestCandleBatches = new List<CandleBatch>();
            _subscriptions = new List<UpdateSubscription>();
            _isInitialized = false;
        }

        public bool Initialize()
        {
            try
            {
                if (_isInitialized) return true;

                var response = _tradingAPIManager.GetAvailableSymbols();
                response.Wait();

                _availableSymbols = response.Result;

                if (_availableSymbols.IsNullOrEmpty())
                {
                    ApplicationEvent?.Invoke(this, new ApplicationEventArgs(EventType.Warning,
                    message: $"Failed to initialize market manager. No available symbols."));

                    return false;
                }

                SetupCandleBatches();

                Task.Run(() => { MonitorCandleBatches(); });

                ApplicationEvent?.Invoke(this, new ApplicationEventArgs(EventType.Information,
                message: $"Initialized market manager."));

                _isInitialized = true;
                return true;
            }
            catch (Exception e)
            {
                ApplicationEvent?.Invoke(this, new ApplicationEventArgs(EventType.Error,
                message: $"!!!Initialization of market manager failed!!! {e}"));

                return false;
            }
        }

        public async Task<IMarket> GetCurrentMarket(string symbol)
        {
            decimal? symbolLatestPrice = await _tradingAPIManager.GetPriceAsync(symbol);
            if (!symbolLatestPrice.HasValue)
                return null;

            lock (_priceClosures)
            {
                List<PriceClosure> symbolPriceClosures = _priceClosures.Where(x => x.Symbol == symbol).OrderByDescending(x => x.CreatedAt).Take(_config.MonitorMarketPriceLevels).ToList();
                if (symbolPriceClosures.IsNullOrEmpty())
                    return null;

                lock (_latestCandleBatches)
                {
                    CandleBatch symbolLatestCandleBatch = _latestCandleBatches.FirstOrDefault(x => x.Symbol == symbol);
                    if (symbolLatestCandleBatch == null)
                        return null;



                    return new Market(symbol, symbolPriceClosures, symbolLatestPrice.Value, symbolLatestCandleBatch.GetAverageVolume() * _config.AverageVolumeWeightFactor);
                }
            }
        }

        public void InvokeAPISubscription()
        {
            if (!_isInitialized) return;

            ApplicationEvent?.Invoke(this, new ApplicationEventArgs(EventType.Information,
            message: $"Invoked API subscription in market manager."));

            BybitSocketClientOptions socketClientOptions = BybitSocketClientOptions.Default;
            socketClientOptions.SpotStreamsV3Options.OutputOriginalData = true;
            socketClientOptions.SpotStreamsV3Options.BaseAddress = _config.SpotStreamEndpoint;

            BybitSocketClient socketClient = new BybitSocketClient(socketClientOptions);

            foreach (var symbol in _availableSymbols)
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

        private void SetupCandleBatches()
        {
            lock (_currentCandleBatches)
            {
                if (!_currentCandleBatches.IsNullOrEmpty())
                {
                    // create a copy and setup again
                    _latestCandleBatches = new List<CandleBatch>(_currentCandleBatches);
                    _currentCandleBatches.Clear();
                }

                foreach (var symbol in _availableSymbols)
                {
                    CandleBatch candleBatch = new CandleBatch(symbol);

                    _currentCandleBatches.Add(candleBatch);
                }
            }
        }

        private void MonitorCandleBatches()
        {
            try
            {
                ApplicationEvent?.Invoke(this, new ApplicationEventArgs(EventType.Information,
                message: $"Started candle batch(es) monitor..."));

                while (true)
                {
                    lock (_currentCandleBatches)
                    {
                        foreach (var symbol in _availableSymbols)
                        {
                            CandleBatch candleBatch = _currentCandleBatches.FirstOrDefault(x => x.Symbol == symbol);
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

                        bool candleBatchesCompleted = _currentCandleBatches.All(x => x.Completed && x.Candles.Count == _config.CandlesInBatch);

                        if (candleBatchesCompleted)
                        {
                            ApplicationEvent?.Invoke(this, new ApplicationEventArgs(EventType.Information,
                            message: $"Completed all symbol candle batch(es). Will start monitoring over again..."));

                            DumpCandleBatches();

                            SetupCandleBatches(); // reset candle batches and start over again
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
            foreach (var candleBatch in _currentCandleBatches)
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
                lock (_currentCandleBatches)
                {
                    CandleBatch candleBatch = _currentCandleBatches.FirstOrDefault(x => x.Symbol == trade.Topic);
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

                    if (symbolTradePrices.Count() >= _config.CreatePriceLevelClosureAfterPriceChanges)
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
                    _priceClosures.Add(priceClosure);

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
