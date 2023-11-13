using Common;
using Common.Events;

namespace MarketProxy.Events
{
    public class PriceEventArgs : BaseEventArgs
    {
        public string Symbol { get; set; }
        public decimal Price { get; set; }

        public PriceEventArgs(string symbol, decimal price) : base(MessageType.Info, "New price received.")
        {
            this.Symbol = symbol;
            this.Price = price;
        }
    }
}
