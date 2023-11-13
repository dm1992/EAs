using Common;
using Common.Events;
using MarketProxy.Models;
using System.Collections.Generic;

namespace MarketProxy.Events
{
    public class TradeEventArgs : BaseEventArgs
    {
        public string Symbol { get; set; }
        public List<Trade> Trades { get; set; }

        public TradeEventArgs(string symbol, List<Trade> trades) : base(MessageType.Info, "New trade received.")
        {
            this.Symbol = symbol;
            this.Trades = trades;
        }
    }
}
