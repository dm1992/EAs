using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CryptoBot.Models
{
    public class MarketMetric
    {
        public MarketMetric(string symbol)
        {
            this.Symbol = symbol;
        }

        public string Symbol { get; set; }
        public MarketVolumeIntensity MarketVolumeIntensity_ExecutedTrades { get; set; }
        public MarketVolumeIntensity MarketVolumeIntensity_ElapsedTime { get; set; }
        public MarketDirection MarketDirection_ExecutedTrades { get; set; }
        public MarketDirection MarketDirection_ElapsedTime { get; set; }

        public bool ValidateMarketVolumeIntensity(out MarketVolumeIntensity marketVolumeIntensity)
        {
            marketVolumeIntensity = MarketVolumeIntensity.Unknown;
            
            if (this.MarketVolumeIntensity_ExecutedTrades != MarketVolumeIntensity.Unknown)
            {
                if (this.MarketVolumeIntensity_ExecutedTrades == this.MarketVolumeIntensity_ElapsedTime)
                {
                    marketVolumeIntensity = this.MarketVolumeIntensity_ExecutedTrades;
                    return true;
                }
            }

            return false;
        }

        public bool ValidateMarketDirection(out MarketDirection marketDirection)
        {
            marketDirection = MarketDirection.Unknown;

            if (this.MarketDirection_ExecutedTrades != MarketDirection.Unknown)
            {
                if (this.MarketDirection_ExecutedTrades == this.MarketDirection_ElapsedTime)
                {
                    marketDirection = this.MarketDirection_ExecutedTrades;
                    return true;
                }
            }

            return false;
        }

        public string Dump()
        {
            return $"{this.Symbol} market metric.\n" +
                   $"Volume intesity - per executed trades: {this.MarketVolumeIntensity_ExecutedTrades}, per elapsed time: {this.MarketVolumeIntensity_ElapsedTime}.\n" +
                   $"Market direction - per executed trades: {this.MarketDirection_ExecutedTrades}, per elapsed time: {this.MarketDirection_ElapsedTime}.";
        }
    }
}
