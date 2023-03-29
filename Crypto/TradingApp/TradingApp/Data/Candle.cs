using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TradingApp.Data
{
    public class Candle
    {
        public DateTime Time { get; set; }
        public string Symbol { get; set; }
        public decimal PriceOpen { get; set; }
        public decimal PriceHigh { get; set; }
        public decimal PriceLow { get; set; }
        public decimal PriceClose { get; set; }
        public decimal Buyers { get; set; }
        public decimal Sellers { get; set; }
        public decimal Delta { get { return this.Buyers - this.Sellers; } }
        public decimal StrengthBuyers { get; set; }
        public decimal StrengthSellers { get; set; }
        public decimal StrengthDelta { get { return this.StrengthBuyers - this.StrengthSellers; } }

        public string Dump()
        {
            return $"{Time}, {Symbol}, {PriceOpen}, {PriceHigh}, {PriceLow}, {PriceClose}, {Buyers}, {Sellers}, {Delta}, {StrengthBuyers}, {StrengthSellers}, {StrengthDelta}";
        }
    }
}
