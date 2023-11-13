using Bybit.Net.Objects.Models.Socket.Spot;
using CryptoExchange.Net.Sockets;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CryptoBot.Data;
using CryptoBot.EventArgs;
using CryptoBot.Interfaces;

namespace CryptoBot.Market
{
    public class CandleAnalyzer : IApplicationEvent
    {
        private readonly Config _config;

        private MarketWatcher _marketWatcher;
        private List<Candle> _candles;
        private List<CandleAnalyzerResult> _candleAnalysisResults;
        private Dictionary<string, bool> _symbolSignals;
        private int _tradingSignalCounter;

        public event EventHandler<ApplicationEventArgs> ApplicationEvent;
        public event EventHandler<TradingSignalEventArgs> TradingSignalEvent;

        public CandleAnalyzer(MarketWatcher marketWatcher, Config config)
        {
            _marketWatcher = marketWatcher;
            _candles = new List<Candle>();
            _candleAnalysisResults = new List<CandleAnalyzerResult>();
            _symbolSignals = new Dictionary<string, bool>();
            _config = config;
            _tradingSignalCounter = 0;

            foreach (var s in _config.Symbols)
            {
                _symbolSignals.Add(s, false);
            }
            
            _marketWatcher.MarketWatcherEvent += MarketWatcherEventHandler;

            Task.Run(() => CandleAnalyzerThread());
        }

        private void CandleAnalyzerThread()
        {
            try
            {
                while (true)
                {
                    foreach (var symbolSignal in _symbolSignals.ToList())
                    {
                        if (!symbolSignal.Value) continue;

                        ToggleSymbolSignal(symbolSignal.Key, false);

                        var symbolCandles = _candles.Where(x => x.Symbol == symbolSignal.Key).OrderByDescending(x => x.Time);

                        if (symbolCandles.Count() < 2)
                        {
                            ApplicationEvent?.Invoke(this, new ApplicationEventArgs(EventType.INFORMATION, 
                                        "Not enough candles to start analyzer. Will wait for next finished candle."));
              
                            continue;
                        }

                        ApplicationEvent?.Invoke(this, new ApplicationEventArgs(EventType.INFORMATION,
                                       $"Analyzing latest finished candle for symbol {symbolSignal.Key}. " +
                                       $"Performing averaging on all {symbolCandles.Count()} symbol finished candles."));

                        var analyzerResult = new CandleAnalyzerResult();
                        analyzerResult.Candle = symbolCandles.First();
                        analyzerResult.AveragingCandleResult = GetAveragingCandleResult(symbolCandles);

                        if (GenerateTradingSignal(analyzerResult, out TradingSignal tradingSignal))
                        {
                            analyzerResult.TradingSignal = tradingSignal;
                        }

                        _candleAnalysisResults.Add(analyzerResult);

                        SaveCandleAnalyzerResult();
                    }
                }
            }
            catch (Exception e)
            {
                ApplicationEvent?.Invoke(this, new ApplicationEventArgs(EventType.ERROR, e.Message));
            }
        }

        private CandleAveragingResult GetAveragingCandleResult(IEnumerable<Candle> candles)
        {
            var acr = new CandleAveragingResult();

            acr.AverageBuyers = candles.Average(x => x.Buyers);            
            acr.AverageSellers = candles.Average(x => x.Sellers);
            acr.AverageStrengthBuyers = candles.Average(x => x.StrengthBuyers);
            acr.AverageStrengthSellers = candles.Average(x => x.StrengthSellers);

            var collection = candles.Where(x => x.Delta > 0);
            acr.AveragePositiveDelta = collection.Any() ? collection.Average(x => x.Delta) : 0;

            collection = candles.Where(x => x.Delta < 0);
            acr.AverageNegativeDelta = collection.Any() ? collection.Average(x => x.Delta) : 0;

            collection = candles.Where(x => x.StrengthDelta > 0);
            acr.AveragePositiveStrengthDelta = collection.Any() ? collection.Average(x => x.StrengthDelta) : 0;

            collection = candles.Where(x => x.StrengthDelta < 0);
            acr.AverageNegativeStrengthDelta = collection.Any() ? collection.Average(x => x.StrengthDelta) : 0;

            return acr;
        }

        private bool GenerateTradingSignal(CandleAnalyzerResult car, out TradingSignal tradingSignal)
        {
            tradingSignal = null;

            var tradingDirection = TradingDirection.NEUTRAL;
            if (car.Candle.Delta > 0 && car.Candle.Delta > car.AveragingCandleResult.AveragePositiveDelta)
            {
                if (car.Candle.StrengthBuyers > car.AveragingCandleResult.AverageStrengthBuyers)
                {
                    tradingDirection = TradingDirection.BUY;
                }
                else if (car.Candle.StrengthBuyers < car.AveragingCandleResult.AverageStrengthBuyers)
                {
                    tradingDirection = TradingDirection.WAIT_SELL;
                }
            }
            else if (car.Candle.Delta < 0 && car.Candle.Delta < car.AveragingCandleResult.AverageNegativeDelta)
            {
                if (car.Candle.StrengthSellers > car.AveragingCandleResult.AverageStrengthSellers)
                {
                    tradingDirection = TradingDirection.SELL;
                }
                else if (car.Candle.StrengthSellers < car.AveragingCandleResult.AverageStrengthSellers)
                {
                    tradingDirection = TradingDirection.WAIT_BUY;
                }
            }

            //xxx generate only strong and very strong signals, define criteria for them!!!
            if (tradingDirection != TradingDirection.NEUTRAL)
            {
                tradingSignal = new TradingSignal();
                tradingSignal.Symbol = car.Candle.Symbol;
                tradingSignal.Direction = tradingDirection;
                tradingSignal.InternalId = ++_tradingSignalCounter;

                TradingSignalEvent?.Invoke(this, new TradingSignalEventArgs(tradingSignal));
                return true;
            }

            return false;
        }

        private void MarketWatcherEventHandler(object sender, MarketWatcherEventArgs args)
        {
            _candles.Add(args.LastCandle);

            ToggleSymbolSignal(args.LastCandle.Symbol, true);
        }

        private void ToggleSymbolSignal(string symbol, bool signaled)
        {
            lock (this)
            {
                _symbolSignals[symbol] = signaled;
            }
        }

        private void SaveCandleAnalyzerResult()
        {
            foreach (var car in _candleAnalysisResults.Where(x => !x.MarkAsSaved))
            {
                if (!Helpers.SaveData($"{car.Dump()}\n", Path.Combine(_config.MarketAnalyzerDataDirectory, $"{car.Candle.Symbol}_analyzerData_{DateTime.Now:ddMMyyyy}.txt"), out string errorReason))
                {
                    ApplicationEvent?.Invoke(this, new ApplicationEventArgs(EventType.WARNING,
                    $"Failed to save candle analyzer result for symbol {car.Candle.Symbol}. Will try to save it later. Reason: {errorReason}"));

                    continue;
                }

                car.MarkAsSaved = true;
            }
        }
    }
}
