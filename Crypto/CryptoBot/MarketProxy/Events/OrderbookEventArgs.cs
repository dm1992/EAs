using Common;
using Common.Events;
using MarketProxy.Models;
using System.Collections.Generic;

namespace MarketProxy.Events
{
    public class OrderbookEventArgs : BaseEventArgs
    {
        public string Symbol { get; set; }
        public Orderbook Orderbook { get; set; }

        public OrderbookEventArgs(string symbol, Orderbook orderbook) : base(MessageType.Info, "New orderbook received.")
        {
            this.Symbol = symbol;
            this.Orderbook = orderbook;
        }
    }
}
