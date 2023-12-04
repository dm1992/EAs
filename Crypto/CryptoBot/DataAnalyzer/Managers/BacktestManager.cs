using Common;
using MarketAnalyzer.Configs;
using MarketAnalyzer.Models;
using NLog;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DataAnalyzer.Managers
{
    public class BacktestManager
    {
        private readonly BacktestConfig _config;
        private readonly ILogger _logger;

        private Dictionary<string, List<HistoryTrade>> _historyTradesBuffer;
        private Dictionary<string, List<MarketSignal>> _marketSignalBuffer;
        private bool _isInitialized;

        public BacktestManager(BacktestConfig config, LogFactory logFactory)
        {
            _config = config;
            _logger = logFactory.GetCurrentClassLogger();

            _historyTradesBuffer = new Dictionary<string, List<HistoryTrade>>();
            _marketSignalBuffer = new Dictionary<string, List<MarketSignal>>();
            _isInitialized = false;
        }

        public bool Initialize()
        {
            if (_isInitialized)
                return true;

            ParseHistoryTrades();

            _logger.Info("Initialized.");
            return _isInitialized = true;
        }

        public void ExecuteBacktest()
        {
            foreach (var symbolConfig in _config.SymbolConfigs)
            {
                if (!GetHistoryTradesFromBuffer(symbolConfig.Symbol, out List<HistoryTrade> historyTrades))
                {
                    _logger.Error($"Failed to execute {symbolConfig.Symbol} backtest. No history trades. Investigate!");
                    continue;
                }
                else if (historyTrades.Count < symbolConfig.HistoryTradesBatchSize)
                {
                    _logger.Error($"Failed to execute {symbolConfig.Symbol} backtest. Total {historyTrades.Count} history trades less than needed {symbolConfig.HistoryTradesBatchSize} history trades batch size.");
                    continue;
                }

                if (symbolConfig.TradingVolume == null)
                {
                    _logger.Error($"Failed to execute {symbolConfig.Symbol} backtest. Entry volume data not found.");
                    continue;
                }

                _logger.Info($"Executing {symbolConfig.Symbol} backtest with total {historyTrades.Count} history trades and {symbolConfig.HistoryTradesBatchSize} history trades batch size.");

                Stopwatch stopwatch = new Stopwatch();
                stopwatch.Start();

                int offset = symbolConfig.HistoryTradesBatchSize;
                decimal latestPrice = 0;

                for (int i = offset; i < historyTrades.Count; i++)
                {
                    List<HistoryTrade> historyTradesBatch = historyTrades.Skip(i - offset).Take(offset).ToList();
                    latestPrice = historyTradesBatch.Last().Price;

                    HandleMarketSignalClosure(symbolConfig.Symbol, latestPrice, force: false);

                    CheckTradingVolumeForMarketEntry(symbolConfig.Symbol, historyTradesBatch);
                }

                HandleMarketSignalClosure(symbolConfig.Symbol, latestPrice, force: true);

                SaveMarketSignals(symbolConfig.Symbol);

                CalculateAndSaveMarketSignalStatistics(symbolConfig.Symbol);
                
                stopwatch.Stop();

                _logger.Debug($"Ended {symbolConfig.Symbol} backtest. Total elapsed time: {stopwatch.Elapsed}.");
            }
        }

        private void SaveMarketSignals(string symbol)
        {
            SymbolConfig symbolConfig = _config.SymbolConfigs.Find(x => x.Symbol == symbol);

            if (symbolConfig == null)
            {
                _logger.Error($"Failed to save market signals on symbol {symbol}. Invalid symbol configuration.");
                return;
            }

            if (!GetMarketSignalsFromBuffer(symbol, out List<MarketSignal> marketSignals))
            {
                _logger.Error($"Failed to get {symbol} market signals in order to save them. Investigate!");
                return;
            }

            string filePath = Path.Combine(symbolConfig.ResultsFilePath, $"{symbol}_marketSignal_{DateTime.Now:ddMMyyyy}.txt");
            string data = String.Join(Environment.NewLine, marketSignals.OrderBy(x => x.CreatedAt).Select(x => x.DumpGeneralInfo())) + Environment.NewLine;

            _logger.Debug($"Saving market signals to: {filePath}.");

            Helpers.WriteToFile(data, filePath);
        }

        private void CalculateAndSaveMarketSignalStatistics(string symbol)
        {
            SymbolConfig symbolConfig = _config.SymbolConfigs.Find(x => x.Symbol == symbol);

            if (symbolConfig == null)
            {
                _logger.Error($"Failed to calculate and save market signal statistics on symbol {symbol}. Invalid symbol configuration.");
                return;
            }

            if (!GetMarketSignalsFromBuffer(symbol, out List<MarketSignal> marketSignals))
            {
                _logger.Error($"Failed to get {symbol} market signals in order to calculate statistics on them. Investigate!");
                return;
            }

            int totalMarketSignals = marketSignals.Count();
            int profitMarketSignals = marketSignals.Where(x => x.ROI > 0).Count();
            int lossMarketSignals = marketSignals.Where(x => x.ROI < 0).Count();
            int neutralMarketSignals = marketSignals.Where(x => x.ROI == 0).Count();

            decimal profitPercentageMarketSignals = Math.Round((profitMarketSignals / (decimal)(profitMarketSignals + lossMarketSignals + neutralMarketSignals)) * 100.0m, 2);
            decimal lossPercentageMarketSignals = Math.Round((lossMarketSignals / (decimal)(profitMarketSignals + lossMarketSignals + neutralMarketSignals)) * 100.0m, 2);
            decimal profitAmountMarketSignals = marketSignals.Where(x => x.ROI > 0).Sum(x => x.ROI);
            decimal lossAmountMarketSignals = marketSignals.Where(x => x.ROI < 0).Sum(x => x.ROI);
            decimal netAmountMarketSignals = lossAmountMarketSignals + profitAmountMarketSignals;

            string filePath = Path.Combine(symbolConfig.ResultsFilePath, $"{symbol}_marketSignalStatistic_{DateTime.Now:ddMMyyyy}.txt");
            string data = $"TOTAL market signals: {totalMarketSignals}\n" +
                          $"PROFIT market signals: {profitMarketSignals} ({profitPercentageMarketSignals}%)\n" +
                          $"LOSS market signals: {lossMarketSignals} ({lossPercentageMarketSignals}%)\n" +
                          $"NET amount: {netAmountMarketSignals}$ -> (PROFIT amount: {profitAmountMarketSignals}$, LOSS amount: {lossAmountMarketSignals}$)"; 

            _logger.Debug($"Saving market signal statistics to: {filePath}.");

            Helpers.WriteToFile(data, filePath);
        }

        private void HandleMarketSignalClosure(string symbol, decimal price, MarketDirection? marketDirection = null)
        {
            HandleMarketSignalClosure(symbol, price, force: true, marketDirection);
        }

        private void HandleMarketSignalClosure(string symbol, decimal price, bool force, MarketDirection? marketDirection = null)
        {
            if (!GetMarketSignalsFromBuffer(symbol, out List<MarketSignal> marketSignals))
            {
                //_logger.Error($"Failed to get {symbol} market signals in order to close them. Investigate!");
                return;
            }

            foreach (var marketSignal in marketSignals)
            {
                if (!marketSignal.IsActive)
                {
                    // already closed market signal, skip it
                    continue;
                }

                bool tryMarketSignalClosure = true;

                if (marketDirection.HasValue)
                {
                    if (marketSignal.MarketDirection != marketDirection)
                    {
                        tryMarketSignalClosure = false;
                    }
                }

                if (tryMarketSignalClosure)
                {
                    if (marketSignal.Close(price, force))
                    {
                        _logger.Info($"Closed: {marketSignal.DumpOnClosure()}");
                    }
                }
            }
        }

        private void CheckTradingVolumeForMarketEntry(string symbol, List<HistoryTrade> historyTradesBatch)
        {
            SymbolConfig symbolConfig = _config.SymbolConfigs.Find(x => x.Symbol == symbol);

            if (symbolConfig == null || symbolConfig.TradingVolume == null)
            {
                _logger.Error($"Failed to check trading volume for market entry on symbol {symbol}. Invalid symbol configuration.");
                return;
            }

            if (historyTradesBatch.IsNullOrEmpty())
            {
                _logger.Warn($"Failed to check trading volume for market entry on symbol {symbol}. Missing history trade batch.");
                return;
            }

            HistoryTrade latestHistoryTrade = historyTradesBatch.Last();

            decimal buyLimitVolume = latestHistoryTrade.GetTotalBidVolume();
            decimal sellLimitVolume = latestHistoryTrade.GetTotalAskVolume();
            decimal buyMarketVolume = historyTradesBatch.Where(x => x.TradeDirection == TradeDirection.Buy).Sum(x => x.Volume);
            decimal sellMarketVolume = historyTradesBatch.Where(x => x.TradeDirection == TradeDirection.Sell).Sum(x => x.Volume);

            MarketDirection? entryMarketDirection = null;

            if (buyMarketVolume > symbolConfig.TradingVolume.BuyMarket)
            {
                if (buyLimitVolume > symbolConfig.TradingVolume.BuyLimit)
                {
                    entryMarketDirection = MarketDirection.Buy;
                }
            }
            else if (sellMarketVolume > symbolConfig.TradingVolume.SellMarket)
            {
                if (sellLimitVolume > symbolConfig.TradingVolume.SellLimit)
                {
                    entryMarketDirection = MarketDirection.Sell;
                }
            }

            if (entryMarketDirection.HasValue)
            {
                HandleMarketSignalOpening(symbol, latestHistoryTrade.Time, latestHistoryTrade.Price, entryMarketDirection.Value);
            }
        }

        private void HandleMarketSignalOpening(string symbol, DateTime time, decimal price, MarketDirection marketDirection)
        {
            SymbolConfig symbolConfig = _config.SymbolConfigs.Find(x => x.Symbol == symbol);

            if (symbolConfig == null)
            {
                _logger.Error($"Failed to handle market signal opening on symbol {symbol}. Invalid symbol configuration.");
                return;
            }

            if (GetMarketSignalsFromBuffer(symbol, out List<MarketSignal> marketSignals))
            {
                if (marketDirection == MarketDirection.Buy)
                {
                    int concurrentBuyMarketSignals = marketSignals.Where(x => x.IsActive && x.MarketDirection == MarketDirection.Buy).Count();
                    int concurrentSellMarketSignals = marketSignals.Where(x => x.IsActive && x.MarketDirection == MarketDirection.Sell).Count();

                    if (concurrentBuyMarketSignals >= symbolConfig.ConcurrentBuyMarketSignals)
                        return;
                    else if (concurrentSellMarketSignals > 0)
                        return;

                    //_logger.Debug($"Closing counter {symbol} market SELL signals, if any are present.");

                    //HandleMarketSignalClosure(symbol, price, MarketDirection.Sell);
                }
                else if (marketDirection == MarketDirection.Sell)
                {
                    int concurrentSellMarketSignals = marketSignals.Where(x => x.IsActive && x.MarketDirection == MarketDirection.Sell).Count();
                    int concurrentBuyMarketSignals = marketSignals.Where(x => x.IsActive && x.MarketDirection == MarketDirection.Buy).Count();

                    if (concurrentSellMarketSignals >= symbolConfig.ConcurrentSellMarketSignals)
                        return;
                    else if (concurrentBuyMarketSignals > 0)
                        return;

                    //_logger.Debug($"Closing counter {symbol} market BUY signals, if any are present.");

                    //HandleMarketSignalClosure(symbol, price, MarketDirection.Buy);
                }
            }
          
            MarketSignal marketSignal = new MarketSignal(symbol, time, price, marketDirection);
            marketSignal.SetTradingFeeAmount(symbolConfig.TradingFeeAmount);
            marketSignal.SetTakeProfitPrice(symbolConfig.TakeProfitAmount);
            marketSignal.SetStopLossPrice(symbolConfig.StopLossAmount);

            _logger.Info($"Created: {marketSignal.DumpOnCreate()}");

            AddMarketSignalToBuffer(symbol, marketSignal);
        }

        private void ParseHistoryTrades()
        {
            foreach (var symbolConfig in _config.SymbolConfigs)
            {
                foreach (string historyTradesFilePath in Directory.EnumerateFiles(symbolConfig.HistoryTradesFilePath, "*.txt"))
                {
                    _logger.Info($"Parsing {symbolConfig.Symbol} history trades from '{historyTradesFilePath}'.");

                    foreach (string historyTradeEntry in Helpers.ReadAllLinesFromFile(historyTradesFilePath))
                    {
                        string[] historyTradeEntryValues = historyTradeEntry.Split(';');

                        HistoryTrade historyTrade = new HistoryTrade();
                        historyTrade.Symbol = historyTradeEntryValues[0];
                        historyTrade.Time = Convert.ToDateTime(historyTradeEntryValues[1]);
                        historyTrade.Price = Convert.ToDecimal(historyTradeEntryValues[2]);
                        historyTrade.Volume = Convert.ToDecimal(historyTradeEntryValues[3]);
                        historyTrade.TradeDirection = (TradeDirection)Enum.Parse(typeof(TradeDirection), historyTradeEntryValues[4], true);
                        historyTrade.AskVolume1 = Convert.ToDecimal(historyTradeEntryValues[5]);
                        historyTrade.AskPrice1 = Convert.ToDecimal(historyTradeEntryValues[6]);
                        historyTrade.AskVolume2 = Convert.ToDecimal(historyTradeEntryValues[7]);
                        historyTrade.AskPrice2 = Convert.ToDecimal(historyTradeEntryValues[8]);
                        historyTrade.AskVolume3 = Convert.ToDecimal(historyTradeEntryValues[9]);
                        historyTrade.AskPrice3 = Convert.ToDecimal(historyTradeEntryValues[10]);
                        historyTrade.BidVolume1 = Convert.ToDecimal(historyTradeEntryValues[11]);
                        historyTrade.BidPrice1 = Convert.ToDecimal(historyTradeEntryValues[12]);
                        historyTrade.BidVolume2 = Convert.ToDecimal(historyTradeEntryValues[13]);
                        historyTrade.BidPrice2 = Convert.ToDecimal(historyTradeEntryValues[14]);
                        historyTrade.BidVolume3 = Convert.ToDecimal(historyTradeEntryValues[15]);
                        historyTrade.BidPrice3 = Convert.ToDecimal(historyTradeEntryValues[16]);

                        AddHistoryTradeToBuffer(symbolConfig.Symbol, historyTrade);
                    }
                }
            }
        }

        private void AddHistoryTradeToBuffer(string symbol, HistoryTrade historyTrade)
        {
            if (historyTrade == null) return;

            if (!_historyTradesBuffer.TryGetValue(symbol, out _))
            {
                _historyTradesBuffer.Add(symbol, new List<HistoryTrade>() { historyTrade });
            }
            else
            {
                _historyTradesBuffer[symbol].Add(historyTrade);
            }
        }

        private bool GetHistoryTradesFromBuffer(string symbol, out List<HistoryTrade> historyTrades)
        {
            if (_historyTradesBuffer.TryGetValue(symbol, out historyTrades))
            {
                return !historyTrades.IsNullOrEmpty();
            }

            return false;
        }

        private void AddMarketSignalToBuffer(string symbol, MarketSignal marketSignal)
        {
            if (marketSignal == null) return;

            if (!_marketSignalBuffer.TryGetValue(symbol, out _))
            {
                _marketSignalBuffer.Add(symbol, new List<MarketSignal>() { marketSignal });
            }
            else
            {
                _marketSignalBuffer[symbol].Add(marketSignal);
            }
        }

        private bool GetMarketSignalsFromBuffer(string symbol, out List<MarketSignal> marketSignals)
        {
            if (_marketSignalBuffer.TryGetValue(symbol, out marketSignals))
            {
                return !marketSignals.IsNullOrEmpty();
            }

            return false;
        }
    }
}
