using Bybit.Net.Clients;
using Bybit.Net.Objects;
using Bybit.Net.Objects.Models.Socket.Spot;
using CryptoBot.Data;
using CryptoBot.EventArgs;
using CryptoBot.Interfaces;
using CryptoBot.Interfaces.Managers;
using CryptoExchange.Net.Sockets;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CryptoBot.Managers.Miha
{
    public class MarketManager : IMarketManager
    {
        private readonly ITradingManager _tradingManager;
        private readonly IOrderManager _orderManager;
        private readonly Config _config;
        private readonly SemaphoreSlim _tradeSemaphore;

        private List<DataEvent<BybitSpotTradeUpdate>> _tradeBuffer;
        private List<TradeCandleBatch> _tradeCandleBatches;
        private List<PriceClosureCandleBatch> _priceClosureCandleBatches;
        private List<string> _availableSymbols;
        private List<UpdateSubscription> _webSocketSubscriptions;
        private bool _isInitialized;

        public event EventHandler<ApplicationEventArgs> ApplicationEvent;

        public MarketManager(ITradingManager tradingManager, IOrderManager orderManager, Config config)
        {
            _tradingManager = tradingManager;
            _orderManager = orderManager;
            _config = config;
            _tradeSemaphore = new SemaphoreSlim(1, 1);

            _tradeBuffer = new List<DataEvent<BybitSpotTradeUpdate>>();
            _tradeCandleBatches = new List<TradeCandleBatch>();
            _priceClosureCandleBatches = new List<PriceClosureCandleBatch>();
            _webSocketSubscriptions = new List<UpdateSubscription>();
            _isInitialized = false;
        }

        public bool Initialize()
        {
            try
            {
                if (_isInitialized) return true;

                var response = _tradingManager.GetAvailableSymbols();
                response.Wait();

                _availableSymbols = response.Result;

                if (_availableSymbols.IsNullOrEmpty())
                {
                    ApplicationEvent?.Invoke(this, new ApplicationEventArgs(EventType.Warning,
                    message: $"Failed to initialize market manager. No available symbols."));

                    return false;
                }

                SetupTradeCandleBatches();
                SetupPriceClosureCandleBatches();

                Task.Run(() => { MonitorTradeCandleBatches(); });

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

        public void InvokeWebSocketEventSubscription()
        {
            if (!_isInitialized) return;

            ApplicationEvent?.Invoke(this, new ApplicationEventArgs(EventType.Information,
            message: $"Invoked web socket event subscription in market manager."));

            BybitSocketClientOptions webSocketOptions = BybitSocketClientOptions.Default;
            webSocketOptions.SpotStreamsV3Options.OutputOriginalData = true;
            webSocketOptions.SpotStreamsV3Options.BaseAddress = _config.SpotStreamEndpoint;

            BybitSocketClient webSocketClient = new BybitSocketClient(webSocketOptions);

            foreach (var symbol in _availableSymbols)
            {
                _webSocketSubscriptions.Add(webSocketClient.SpotStreamsV3.SubscribeToTradeUpdatesAsync(symbol, HandleTrade).GetAwaiter().GetResult().Data); // deadlock issue, async method in sync manner
            }

            foreach (var wss in _webSocketSubscriptions)
            {
                wss.ConnectionRestored += WebSocketSubscription_ConnectionRestored;
                wss.ConnectionLost += WebSocketSubscription_ConnectionLost;
                wss.ConnectionClosed += WebSocketSubscription_ConnectionClosed;
            }
        }

        public async void CloseWebSocketEventSubscription()
        {
            ApplicationEvent?.Invoke(this, new ApplicationEventArgs(EventType.Information,
            message: $"Closed web socket event subscription in market manager."));

            foreach (var wss in _webSocketSubscriptions)
            {
                await wss.CloseAsync();
            }
        }

        private void SetupTradeCandleBatches()
        {
            lock (_tradeCandleBatches)
            {
                _tradeCandleBatches.Clear();

                foreach (var symbol in _availableSymbols)
                {
                    _tradeCandleBatches.Add(new TradeCandleBatch(symbol));
                }
            }
        }

        private void SetupPriceClosureCandleBatches()
        {
            _priceClosureCandleBatches.Clear();

            foreach (var symbol in _availableSymbols)
            {
                _priceClosureCandleBatches.Add(new PriceClosureCandleBatch(symbol));
            }
        }

        private void MonitorTradeCandleBatches()
        {
            try
            {
                ApplicationEvent?.Invoke(this, new ApplicationEventArgs(EventType.Information,
                message: $"Started trade candle batches monitor."));

                while (true)
                {
                    lock (_tradeCandleBatches)
                    {
                        foreach (var symbol in _availableSymbols)
                        {
                            TradeCandleBatch tradeCandleBatch = _tradeCandleBatches.FirstOrDefault(x => x.Symbol == symbol);
                            if (tradeCandleBatch == null)
                            {
                                ApplicationEvent?.Invoke(this, new ApplicationEventArgs(EventType.Error,
                                message: $"!!!Unable to find candle batch for symbol {symbol}."));

                                continue;
                            }

                            // find first active candle inside active candle batch and check it
                            TradeCandle activeTradeCandle = tradeCandleBatch.TradeCandles.FirstOrDefault(x => !x.Completed);
                            if (activeTradeCandle != null)
                            {
                                if ((DateTime.Now - activeTradeCandle.CreatedAt).TotalMinutes >= _config.TradeCandleMinuteTimeframe)
                                {
                                    activeTradeCandle.Completed = true;

                                    ApplicationEvent?.Invoke(this, 
                                    new ApplicationEventArgs(EventType.Information,
                                    message: $"{activeTradeCandle.Dump()}\n\n{activeTradeCandle.DumpTrades()}", 
                                    messageScope: $"verbose_{activeTradeCandle.Symbol}"));
                                }
                            }
                        }

                        bool tradeCandleBatchesCompleted = _tradeCandleBatches.All(x => x.Completed && x.TradeCandles.Count == _config.CandlesInTradeBatch);

                        if (tradeCandleBatchesCompleted)
                        {
                            ApplicationEvent?.Invoke(this, new ApplicationEventArgs(EventType.Information,
                            message: $"Completed all symbol trade candle batches. Will start monitoring over again..."));

                            DumpTradeCandleBatches();
                            SetupTradeCandleBatches(); // reset trade candle batches and start over again
                        }
                    }
                }
            }
            catch (Exception e)
            {
                ApplicationEvent?.Invoke(this, new ApplicationEventArgs(EventType.Error,
                message: $"!!!MonitorTradeCandleBatches failed!!! {e}"));

                DumpTradeCandleBatches();
            }
        }

        private void DumpTradeCandleBatches()
        {
            foreach (var tradeCandleBatch in _tradeCandleBatches)
            {
                ApplicationEvent?.Invoke(this, new ApplicationEventArgs(EventType.Information,
                message: $"{tradeCandleBatch.Dump()}"));
            }
        }

        private bool HandleRegularCandle(DataEvent<BybitSpotTradeUpdate> trade)
        {
            return AddTradeToCandle(trade);
        }

        private bool AddTradeToCandle(DataEvent<BybitSpotTradeUpdate> trade)
        {
            if (trade == null) return false;

            try
            {
                lock (_tradeCandleBatches)
                {
                    TradeCandleBatch tradeCandleBatch = _tradeCandleBatches.FirstOrDefault(x => x.Symbol == trade.Topic);
                    if (tradeCandleBatch == null)
                    {
                        ApplicationEvent?.Invoke(this, new ApplicationEventArgs(EventType.Error,
                        message: $"!!!Unable to find trade candle batch for symbol {trade.Topic}."));

                        return false;
                    }

                    if (tradeCandleBatch.TradeCandles.Count == _config.CandlesInTradeBatch)
                    {
                        if (tradeCandleBatch.Completed)
                        {
                            // trade candle batch for this symbol is completed, exit
                            return false;
                        }
                    }

                    TradeCandle activeTradeCandle = tradeCandleBatch.TradeCandles.FirstOrDefault(x => !x.Completed);
                    if (activeTradeCandle == null)
                    {
                        activeTradeCandle = new TradeCandle(trade.Topic);
                        tradeCandleBatch.TradeCandles.Add(activeTradeCandle);
                    }

                    activeTradeCandle.TradeBuffer.Add(trade);
                    return true;
                }
            }
            catch (Exception e)
            {
                ApplicationEvent?.Invoke(this, new ApplicationEventArgs(EventType.Error,
                message: $"!!!AddTradeToCandle failed!!! {e}"));

                return false;
            }
        }

        private bool HandlePriceClosureCandle(DataEvent<BybitSpotTradeUpdate> trade)
        {
            if (!CreatePriceClosure(trade, out PriceClosure priceClosure))
                return false;

            return AddPriceClosureToCandle(priceClosure);
        }

        private bool CreatePriceClosure(DataEvent<BybitSpotTradeUpdate> trade, out PriceClosure priceClosure)
        {
            priceClosure = null;

            try
            {
                if (trade == null) return false;

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

                if (!closePrice) return false;

                decimal symbolLatestPrice = symbolTrades.Last().Data.Price;
                decimal symbolClosePrice = symbolTradePrices.First();
                List<DataEvent<BybitSpotTradeUpdate>> symbolClosePriceTrades = symbolTrades.Where(x => x.Data.Price == symbolClosePrice).ToList();

                priceClosure = new PriceClosure(trade.Topic, symbolLatestPrice, symbolClosePriceTrades);

                ApplicationEvent?.Invoke(this, new ApplicationEventArgs(EventType.Information,
                message: $"{priceClosure.Dump()}",
                messageScope: $"priceClosure_{trade.Topic}"));

                _tradeBuffer.RemoveAll(x => x.Topic == trade.Topic && x.Data.Price == symbolClosePrice);

                return true;
            }
            catch (Exception e)
            {
                ApplicationEvent?.Invoke(this, new ApplicationEventArgs(EventType.Error,
                message: $"!!!CreatePriceClosure failed!!! {e}"));

                return false;
            }
        }

        private bool AddPriceClosureToCandle(PriceClosure priceClosure)
        {
            if (priceClosure == null) return false;

            try
            {
                PriceClosureCandleBatch priceClosureCandleBatch = _priceClosureCandleBatches.FirstOrDefault(x => x.Symbol == priceClosure.Symbol);
                if (priceClosureCandleBatch == null)
                {
                    ApplicationEvent?.Invoke(this, new ApplicationEventArgs(EventType.Error,
                    message: $"!!!Unable to find price closure candle batch for symbol {priceClosure.Symbol}."));

                    return false;
                }

                PriceClosureCandle activePriceClosureCandle = priceClosureCandleBatch.PriceClosureCandles.FirstOrDefault(x => !x.Completed);
                if (activePriceClosureCandle == null)
                {
                    activePriceClosureCandle = new PriceClosureCandle(priceClosure.Symbol);
                    priceClosureCandleBatch.PriceClosureCandles.Add(activePriceClosureCandle);
                }

                activePriceClosureCandle.PriceClosures.Add(priceClosure);

                if (activePriceClosureCandle.PriceClosures.Count() >= _config.PriceClosureCandleSize)
                {
                    activePriceClosureCandle.Completed = true;
                }

                return true;
            }
            catch (Exception e)
            {
                ApplicationEvent?.Invoke(this, new ApplicationEventArgs(EventType.Error,
                message: $"!!!AddPriceClosureToCandle failed!!! {e}"));

                return false;
            }
        }

        private bool GetMassiveVolumeMarketEntity(string symbol, out MarketEntity marketEntity)
        {
            marketEntity = MarketEntity.Unknown;

            PriceClosureCandleBatch symbolPriceClosureCandleBatch = _priceClosureCandleBatches.FirstOrDefault(x => x.Symbol == symbol);
            if (symbolPriceClosureCandleBatch == null)
            {
                ApplicationEvent?.Invoke(this, new ApplicationEventArgs(EventType.Error,
                message: $"!!!Unable to detect massive volume market entity for symbol {symbol}."));

                return false;
            }

            var symbolLatestPriceClosures = symbolPriceClosureCandleBatch.GetTotalPriceClosures().Take(_config.MarketPriceClosuresOnMassiveVolumeDetection);
            if (symbolLatestPriceClosures.IsNullOrEmpty())
            {
                // nothing to do yet
                return false;
            }

            decimal symbolWeightedTotalAverageBuyerVolume = symbolPriceClosureCandleBatch.GetTotalAverageBuyerVolume() * _config.AverageVolumeWeightFactor;
            decimal symbolWeightedTotalAverageSellerVolume = symbolPriceClosureCandleBatch.GetTotalAverageSellerVolume() * _config.AverageVolumeWeightFactor;
            int massiveVolumeBuyers = 0;
            int massiveVolumeSellers = 0;

            foreach (var symbolPriceClosure in symbolLatestPriceClosures)
            {
                if (symbolPriceClosure.BuyerVolume > symbolWeightedTotalAverageBuyerVolume)
                {
                    massiveVolumeBuyers++;
                }
                else if (symbolPriceClosure.SellerVolume > symbolWeightedTotalAverageSellerVolume)
                {
                    massiveVolumeSellers++;
                }
            }

            decimal massiveVolumeBuyersPercent = (massiveVolumeBuyers / symbolLatestPriceClosures.Count()) * 100.0M;
            if (massiveVolumeBuyersPercent < _config.MassiveBuyersPercentLimit)
            {
                // limit not reached, set to 0
                massiveVolumeBuyersPercent = 0;
            }

            decimal massiveVolumeSellersPercent = (massiveVolumeSellers / symbolLatestPriceClosures.Count()) * 100.0M;
            if (massiveVolumeSellersPercent < _config.MassiveSellersPercentLimit)
            {
                // limit not reached, set to 0
                massiveVolumeSellersPercent = 0;
            }

            if (massiveVolumeBuyersPercent > massiveVolumeSellersPercent)
            {
                marketEntity = MarketEntity.Buyer;
            }
            else if (massiveVolumeSellersPercent > massiveVolumeBuyersPercent)
            {
                marketEntity = MarketEntity.Seller;
            }

            if (marketEntity == MarketEntity.Unknown)
                return false;

            ApplicationEvent?.Invoke(this, new ApplicationEventArgs(EventType.Information,
            message: $"{symbol} massive market volume entity {marketEntity} in {symbolLatestPriceClosures.Count()} latest price closures. " +
            $"Massive volume buyers %: {Math.Round(massiveVolumeBuyersPercent, 3)}. Massive volume sellers %: {Math.Round(massiveVolumeSellersPercent, 3)}.",
            messageScope: $"marketMetric_{symbol}"));

            return true;
        }

        private bool GetMarketDirection(string symbol, out MarketDirection marketDirection)
        {
            marketDirection = MarketDirection.Unknown;

            PriceClosureCandleBatch symbolPriceClosureCandleBatch = _priceClosureCandleBatches.FirstOrDefault(x => x.Symbol == symbol);
            if (symbolPriceClosureCandleBatch == null)
            {
                ApplicationEvent?.Invoke(this, new ApplicationEventArgs(EventType.Error,
                message: $"!!!Unable to get market direction for symbol {symbol}."));

                return false;
            }

            if (symbolPriceClosureCandleBatch.PriceClosureCandles.Count() < _config.MarketPriceClosureCandlesOnMarketDirectionDetection)
            {
                // nothing to do yet
                return false;
            }

            var symbolLatestPriceClosurePriceMove = symbolPriceClosureCandleBatch.GetLatestPriceClosurePriceMove();
            decimal symbolWeightedPositiveAveragePriceMove = symbolPriceClosureCandleBatch.GetPositiveAveragePriceMove() * _config.AveragePriceMoveWeightFactor;
            decimal symbolWeightedNegativeAveragePriceMove = symbolPriceClosureCandleBatch.GetNegativeAveragePriceMove() * _config.AveragePriceMoveWeightFactor;

            if (symbolLatestPriceClosurePriceMove > symbolWeightedPositiveAveragePriceMove)
            {
                marketDirection = MarketDirection.Uptrend;
            }
            else if (symbolLatestPriceClosurePriceMove < symbolWeightedNegativeAveragePriceMove)
            {
                marketDirection = MarketDirection.Downtrend;
            }

            if (marketDirection == MarketDirection.Unknown)
                return false;

            ApplicationEvent?.Invoke(this, new ApplicationEventArgs(EventType.Information,
            message: $"{symbol} market direction {marketDirection}. " +
            $"Latest price closure price move: {symbolLatestPriceClosurePriceMove}. " +
            $"Weighted positive price move: {symbolWeightedPositiveAveragePriceMove}. Weighted negative price move: {symbolWeightedNegativeAveragePriceMove}.",
            messageScope: $"marketMetric_{symbol}"));

            return true;
        }

        private async Task<bool> InvokeMarketOrder(string symbol)
        {
            if (String.IsNullOrEmpty(symbol))
            {
                ApplicationEvent?.Invoke(this, new ApplicationEventArgs(EventType.Error,
                message: $"!!!Unable to invoke market order of unknown symbol."));

                return false;
            }

            if (!GetMarketDirection(symbol, out MarketDirection marketDirection))
                return false;

            if (!GetMassiveVolumeMarketEntity(symbol, out _)) //xxx for now we don't care about massive volume market entity
                return false;

            return await _orderManager.InvokeOrder(symbol, marketDirection);
        }


        #region Event handlers

        private void WebSocketSubscription_ConnectionRestored(TimeSpan obj)
        {
            ApplicationEvent?.Invoke(this, new ApplicationEventArgs(EventType.Information,
            message: $"Web socket subscription connection restored."));
        }

        private void WebSocketSubscription_ConnectionLost()
        {
            ApplicationEvent?.Invoke(this, new ApplicationEventArgs(EventType.Information,
            message: $"Web socket subscription connection lost."));
        }

        private void WebSocketSubscription_ConnectionClosed()
        {
            ApplicationEvent?.Invoke(this, new ApplicationEventArgs(EventType.Information,
            message: $"Web socket subscription connection closed."));
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

                HandleRegularCandle(trade);
                
                if (HandlePriceClosureCandle(trade))
                {
                    // price closure handled. Check for potential market order invocation.
                    InvokeMarketOrder(trade.Topic).GetAwaiter().GetResult();
                }
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
