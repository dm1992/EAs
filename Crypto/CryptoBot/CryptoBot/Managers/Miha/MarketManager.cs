using Bybit.Net.Clients;
using Bybit.Net.Enums;
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

                _availableSymbols = response.Result.ToList();

                if (_availableSymbols.IsNullOrEmpty())
                {
                    throw new Exception("No available symbols.");
                }

                SetupTradeCandleBatches();
                SetupPriceClosureCandleBatches();

                Task.Run(() => { MonitorTradeCandleBatches(); });

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

        public void InvokeWebSocketEventSubscription()
        {
            if (!_isInitialized) return;

            ApplicationEvent?.Invoke(this, new MarketManagerEventArgs(EventType.Information, "Invoked web socket event subscription."));

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
            ApplicationEvent?.Invoke(this, new MarketManagerEventArgs(EventType.Information, "Closed web socket event subscription."));

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
                ApplicationEvent?.Invoke(this, new MarketManagerEventArgs(EventType.Information, "Started trade candle batches monitor."));

                while (true)
                {
                    lock (_tradeCandleBatches)
                    {
                        foreach (var symbol in _availableSymbols)
                        {
                            TradeCandleBatch batch = _tradeCandleBatches.FirstOrDefault(x => x.Symbol == symbol);
                            if (batch == null)
                            {
                                ApplicationEvent?.Invoke(this, new MarketManagerEventArgs(EventType.Error, $"!!!Unable to find candle batch for symbol {symbol}."));
                                continue;
                            }

                            // find first active candle inside active candle batch and check it
                            TradeCandle candle = batch.TradeCandles.FirstOrDefault(x => !x.Completed);
                            if (candle != null)
                            {
                                if ((DateTime.Now - candle.CreatedAt).TotalMinutes >= _config.TradeCandleMinuteTimeframe)
                                {
                                    candle.Completed = true;

                                    ApplicationEvent?.Invoke(this, new MarketManagerEventArgs(EventType.Information, $"{candle.Dump()}\n\n{candle.DumpTrades()}", messageSubTag: "tradeCandle"));
                                }
                            }
                        }

                        bool allBatchesCompleted = _tradeCandleBatches.All(x => x.Completed && x.TradeCandles.Count == _config.CandlesInTradeBatch);

                        if (allBatchesCompleted)
                        {
                            ApplicationEvent?.Invoke(this, new MarketManagerEventArgs(EventType.Information, "Completed all symbol trade candle batches. Will start monitoring over again."));

                            DumpTradeCandleBatches();
                            SetupTradeCandleBatches(); // reset trade candle batches and start over again
                        }
                    }
                }
            }
            catch (Exception e)
            {
                ApplicationEvent?.Invoke(this, new MarketManagerEventArgs(EventType.Error, $"!!!MonitorTradeCandleBatches failed!!! {e}"));

                DumpTradeCandleBatches();
            }
        }

        private void DumpTradeCandleBatches()
        {
            foreach (TradeCandleBatch tradeCandleBatch in _tradeCandleBatches)
            {
                ApplicationEvent?.Invoke(this, new MarketManagerEventArgs(EventType.Information, $"{tradeCandleBatch.Dump()}"));
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
                    TradeCandleBatch batch = _tradeCandleBatches.FirstOrDefault(x => x.Symbol == trade.Topic);
                    if (batch == null)
                    {
                        ApplicationEvent?.Invoke(this, new MarketManagerEventArgs(EventType.Error, $"!!!Unable to find trade candle batch for symbol {trade.Topic}."));

                        return false;
                    }

                    if (batch.TradeCandles.Count == _config.CandlesInTradeBatch)
                    {
                        if (batch.Completed)
                        {
                            // trade candle batch for this symbol is completed, exit
                            return false;
                        }
                    }

                    TradeCandle candle = batch.TradeCandles.FirstOrDefault(x => !x.Completed);
                    if (candle == null)
                    {
                        candle = new TradeCandle(trade.Topic);
                        batch.TradeCandles.Add(candle);
                    }

                    candle.TradeBuffer.Add(trade);
                    return true;
                }
            }
            catch (Exception e)
            {
                ApplicationEvent?.Invoke(this, new MarketManagerEventArgs(EventType.Error, $"!!!AddTradeToCandle failed!!! {e}"));
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

                bool createPriceClosure = false;

                var tradePrices = new List<decimal>();
                var tradeBuffer = _tradeBuffer.Where(x => x.Topic == trade.Topic).OrderBy(x => x.Data.Timestamp); // ordered trades

                foreach (var tb in tradeBuffer)
                {
                    if (!tradePrices.Contains(tb.Data.Price))
                    {
                        tradePrices.Add(tb.Data.Price);
                    }

                    if (tradePrices.Count() >= _config.CreatePriceLevelClosureAfterPriceChanges)
                    {
                        createPriceClosure = true;
                        break;
                    }
                }

                if (!createPriceClosure) return false;

                decimal latestPrice = tradeBuffer.Last().Data.Price;
                decimal closePrice = tradePrices.First();
                List<DataEvent<BybitSpotTradeUpdate>> closePriceTrades = tradeBuffer.Where(x => x.Data.Price == closePrice).ToList();

                priceClosure = new PriceClosure(trade.Topic, latestPrice, closePriceTrades);

                _tradeBuffer.RemoveAll(x => x.Topic == trade.Topic && x.Data.Price == closePrice);
                return true;
            }
            catch (Exception e)
            {
                ApplicationEvent?.Invoke(this, new MarketManagerEventArgs(EventType.Error, $"!!!CreatePriceClosure failed!!! {e}"));
                return false;
            }
        }

        private bool AddPriceClosureToCandle(PriceClosure priceClosure)
        {
            if (priceClosure == null) return false;

            try
            {
                PriceClosureCandleBatch batch = _priceClosureCandleBatches.FirstOrDefault(x => x.Symbol == priceClosure.Symbol);
                if (batch == null)
                {
                    ApplicationEvent?.Invoke(this, new MarketManagerEventArgs(EventType.Error, $"!!!Unable to find price closure candle batch for symbol {priceClosure.Symbol}."));
                    return false;
                }

                PriceClosureCandle candle = batch.PriceClosureCandles.FirstOrDefault(x => !x.Completed);
                if (candle == null)
                {
                    candle = new PriceClosureCandle(priceClosure.Symbol);
                    batch.PriceClosureCandles.Add(candle);
                }

                candle.PriceClosures.Add(priceClosure);

                if (candle.PriceClosures.Count() >= _config.PriceClosureCandleSize)
                {
                    candle.Completed = true;

                    ApplicationEvent?.Invoke(this, new MarketManagerEventArgs(EventType.Information, $"{candle.Dump()}\n\n{candle.DumpPriceClosures()}", messageSubTag: "priceClosureCandle"));
                }

                return true;
            }
            catch (Exception e)
            {
                ApplicationEvent?.Invoke(this, new MarketManagerEventArgs(EventType.Error, $"!!!AddPriceClosureToCandle failed!!! {e}"));
                return false;
            }
        }

        private bool GetMassiveVolumeMarketEntity(string symbol, out MarketEntity marketEntity)
        {
            marketEntity = MarketEntity.Unknown;

            PriceClosureCandleBatch batch = _priceClosureCandleBatches.FirstOrDefault(x => x.Symbol == symbol);
            if (batch == null)
            {
                ApplicationEvent?.Invoke(this, new MarketManagerEventArgs(EventType.Error, $"!!!Unable to detect massive volume market entity for symbol {symbol}."));
                return false;
            }

            var latestPriceClosures = batch.GetTotalPriceClosures().Take(_config.MarketPriceClosuresOnMassiveVolumeDetection);
            if (latestPriceClosures.IsNullOrEmpty())
            {
                // nothing to do yet
                return false;
            }

            decimal averageBuyerVolume = batch.GetTotalAverageBuyerVolume() * _config.AverageVolumeWeightFactor;
            decimal averageSellerVolume = batch.GetTotalAverageSellerVolume() * _config.AverageVolumeWeightFactor;
            int massiveVolumeBuyers = 0;
            int massiveVolumeSellers = 0;

            foreach (PriceClosure priceClosure in latestPriceClosures)
            {
                if (priceClosure.BuyerVolume > averageBuyerVolume)
                {
                    massiveVolumeBuyers++;
                }
                else if (priceClosure.SellerVolume > averageSellerVolume)
                {
                    massiveVolumeSellers++;
                }
            }

            decimal massiveVolumeBuyersPercent = (massiveVolumeBuyers / (decimal)latestPriceClosures.Count()) * 100.0M;
            if (massiveVolumeBuyersPercent < _config.MassiveBuyersPercentLimit)
            {
                // limit not reached, set to 0
                massiveVolumeBuyersPercent = 0;
            }

            decimal massiveVolumeSellersPercent = (massiveVolumeSellers / (decimal)latestPriceClosures.Count()) * 100.0M;
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
            {
                // nothing to do
                return false;
            }

            ApplicationEvent?.Invoke(this, new MarketManagerEventArgs(EventType.Information, $"{marketEntity},{batch.Dump()}", messageSubTag: "marketMetric"));
            return true;
        }

        private bool GetMarketDirection(string symbol, out MarketDirection marketDirection)
        {
            marketDirection = MarketDirection.Unknown;

            PriceClosureCandleBatch batch = _priceClosureCandleBatches.FirstOrDefault(x => x.Symbol == symbol);
            if (batch == null)
            {
                ApplicationEvent?.Invoke(this, new MarketManagerEventArgs(EventType.Error, $"!!!Unable to get market direction for symbol {symbol}."));
                return false;
            }

            if (batch.PriceClosureCandles.Count() < _config.MarketPriceClosureCandlesOnMarketDirectionDetection)
            {
                // nothing to do yet
                return false;
            }

            decimal latestPriceClosurePriceMove = batch.GetLatestPriceClosurePriceMove();
            decimal positiveAveragePriceMove = batch.GetPositiveAveragePriceMove() * _config.AveragePriceMoveWeightFactor;
            decimal negativeAveragePriceMove = batch.GetNegativeAveragePriceMove() * _config.AveragePriceMoveWeightFactor;

            if (latestPriceClosurePriceMove > positiveAveragePriceMove)
            {
                marketDirection = MarketDirection.Uptrend;
            }
            else if (latestPriceClosurePriceMove < negativeAveragePriceMove)
            {
                marketDirection = MarketDirection.Downtrend;
            }

            if (marketDirection == MarketDirection.Unknown)
            {
                // nothing to do
                return false;
            }

            ApplicationEvent?.Invoke(this, new MarketManagerEventArgs(EventType.Information, $"{marketDirection},{batch.Dump()}", messageSubTag: "marketMetric"));
            return true;
        }

        private async Task<bool> InvokeMarketOrder(string symbol)
        {
            if (String.IsNullOrEmpty(symbol))
            {
                ApplicationEvent?.Invoke(this, new MarketManagerEventArgs(EventType.Error, "!!!Unable to invoke market order of unknown symbol."));
                return false;
            }

            if (!GetMarketDirection(symbol, out MarketDirection marketDirection))
                return false;

            if (!GetMassiveVolumeMarketEntity(symbol, out _)) //xxx for now we don't care about massive volume market entity
                return false;

            OrderSide marketOrderSide = marketDirection == MarketDirection.Uptrend ? OrderSide.Sell : OrderSide.Buy;

            return await _orderManager.InvokeOrder(symbol, marketOrderSide);
        }


        #region Event handlers

        private void WebSocketSubscription_ConnectionRestored(TimeSpan obj)
        {
            ApplicationEvent?.Invoke(this, new MarketManagerEventArgs(EventType.Information, "Web socket subscription connection restored."));
        }

        private void WebSocketSubscription_ConnectionLost()
        {
            ApplicationEvent?.Invoke(this, new MarketManagerEventArgs(EventType.Information, "Web socket subscription connection lost."));
        }

        private void WebSocketSubscription_ConnectionClosed()
        {
            ApplicationEvent?.Invoke(this, new MarketManagerEventArgs(EventType.Information, "Web socket subscription connection closed."));
        }

        private async void HandleTrade(DataEvent<BybitSpotTradeUpdate> trade)
        {
            try
            {
                await _tradeSemaphore.WaitAsync();

                trade.Topic = (string)Extensions.ParseObject(trade.OriginalData, "topic");
                if (trade.Topic == null)
                {
                    ApplicationEvent?.Invoke(this, new MarketManagerEventArgs(EventType.Error, "!!!Unable to parse topic from original trade data."));
                    return;
                }

                _tradeBuffer.Add(trade);

                HandleRegularCandle(trade);
                
                if (HandlePriceClosureCandle(trade))
                {
                    // price closure handled. Check for potential market order invocation.
                    await InvokeMarketOrder(trade.Topic);
                }
            }
            catch (Exception e)
            {
                ApplicationEvent?.Invoke(this, new MarketManagerEventArgs(EventType.Error, $"!!!HandleTrade failed!!! {e}"));
            }
            finally
            {
                _tradeSemaphore.Release();
            }
        }

        #endregion
    }
}
