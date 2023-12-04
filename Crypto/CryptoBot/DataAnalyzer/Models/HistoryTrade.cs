using Common;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MarketAnalyzer.Models
{
    public class HistoryTrade
    {
        public string Symbol { get; set; }
        public DateTime Time { get; set; }
        public decimal Price { get; set; }
        public decimal Volume { get; set; }
        public TradeDirection TradeDirection { get; set; }
        public decimal AskVolume1 { get; set; }
        public decimal AskPrice1 { get; set; }
        public decimal AskVolume2 { get; set; }
        public decimal AskPrice2 { get; set; }
        public decimal AskVolume3 { get; set; }
        public decimal AskPrice3 { get; set; }
        public decimal BidVolume1 { get; set; }
        public decimal BidPrice1 { get; set; }
        public decimal BidVolume2 { get; set; }
        public decimal BidPrice2 { get; set; }
        public decimal BidVolume3 { get; set; }
        public decimal BidPrice3 { get; set; }

        public decimal GetTotalAskVolume()
        {
            return this.AskVolume1 + this.AskVolume2 + this.AskVolume3;
        }

        public decimal GetTotalBidVolume()
        {
            return this.BidVolume1 + this.BidVolume2 + this.BidVolume3;
        }
    }
}
