using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CryptoBot.Models
{
    public class MarketSignal
    {
        public string Symbol { get; set; }
        public bool IsCounter { get; set; }
        public decimal EntryPrice { get; set; }
        public decimal ExitPrice { get; set; }

        private decimal CalculateROI()
        {
            if (this.MarketDirection == MarketDirection.Uptrend)
            {
                return this.ExitPrice - this.EntryPrice;
            }
            else if (this.MarketDirection == MarketDirection.Downtrend)
            {
                return this.EntryPrice - this.ExitPrice;
            }

            return 0;
        }
        public decimal ROI { get { return CalculateROI(); } }

        public MarketDirection MarketDirection { get; set; }
        public MarketInformation MarketInformation { get; set; }
                                                               
        public MarketSignal(string symbol)
        {
            this.Symbol = symbol;
        }

        public string Dump()
        {
            return $"CURRENT {this.Symbol} MARKET SIGNAL {(this.MarketDirection == MarketDirection.Uptrend ? "BUY" : "SELL")} @ {this.EntryPrice}$";
        }


        public string DumpOnCreate()
        {
            return $"!!! CREATED {this.Symbol} MARKET SIGNAL {(this.MarketDirection == MarketDirection.Uptrend ? "BUY" : "SELL")} @ {this.EntryPrice}$ !!!";
        }

        public string DumpOnRemove()
        {
            return $"!!! [{(this.ROI > 0 ? "PROFIT" : "LOSS")}] {this.Symbol} MARKET SIGNAL {(this.MarketDirection == MarketDirection.Uptrend ? "BUY" : "SELL")} @ {this.ExitPrice}$ with ROI {this.ROI}$ !!!";
        }
    }
}
