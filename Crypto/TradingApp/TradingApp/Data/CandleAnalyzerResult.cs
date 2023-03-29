using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TradingApp.Data
{
    public class CandleAnalyzerResult
    {
        public Candle Candle { get; set; }
        public CandleAveragingResult AveragingCandleResult { get; set; }
        public TradingSignal TradingSignal { get; set; }

        public bool MarkAsSaved { get; set; } = false;

        public string Dump()
        {
            return $"{Candle.Time}, {Candle.Symbol}, {Candle.PriceOpen}, {Candle.PriceHigh}, {Candle.PriceLow}, {Candle.PriceClose}, " +
                   $"{Candle.Buyers}, {AveragingCandleResult.AverageBuyers}, {Candle.Sellers}, {AveragingCandleResult.AverageSellers}, " +
                   $"{Candle.Delta}, {AveragingCandleResult.AveragePositiveDelta}, {AveragingCandleResult.AverageNegativeDelta}, " +
                   $"{Candle.StrengthBuyers}, {AveragingCandleResult.AverageStrengthBuyers}, {Candle.StrengthSellers}, {AveragingCandleResult.AverageStrengthSellers}, " +
                   $"{Candle.StrengthDelta}, {AveragingCandleResult.AveragePositiveStrengthDelta}, {AveragingCandleResult.AverageNegativeStrengthDelta}" +
                   $"{(TradingSignal == null ? String.Empty : String.Concat(", ", TradingSignal.ToString()))}";
        }
    }
}
