using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TradeCollectorApp.Models
{
    public class Trade
    {
        public long Id { get; set; }
        public string Symbol { get; set; }
        public decimal Price { get; set; }
        public decimal Quantity { get; set; }
        public DateTime Timestamp { get; set; }
        public TradeType Type { get; set; }
        public decimal BidPrice { get; set; } = -1;
        public decimal BidQuantity { get; set; } = -1;
        public decimal AskPrice { get; set; } = -1;
        public decimal AskQuantity { get; set; } = -1;

        public override string ToString()
        {
            return $"{Id}, {Price}, {Quantity}, {Timestamp}, {Type}, {BidPrice}, {BidQuantity}, {AskPrice}, {AskQuantity}\n";
        }
    }

    public enum TradeType
    {
        NONE = 0,
        BUY = 1,
        SELL = 2
    }
}
