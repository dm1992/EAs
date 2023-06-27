using Bybit.Net.Objects.Models.V5;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CryptoBot.Models
{
    [Obsolete]
    public class TradingSignal
    {
        public string Symbol { get; set; }
        public decimal Price { get; set; }
        public decimal TakeProfitPrice { get; set; }
        public decimal StopLossPrice { get; set; }
        public MarketDirection MarketDirection { get; set; }
        public DateTime CreatedAt { get; set; }
        public bool Active { get; set; }
        public BybitOrderbook Orderbook { get; set; }
        public BybitSpotTickerUpdate Ticker { get; set; }

        public TradingSignal(string symbol, decimal price, MarketDirection marketDirection)
        {
            this.Symbol = symbol;
            this.Price = price;
            this.MarketDirection = marketDirection;
            this.TakeProfitPrice = marketDirection == MarketDirection.Uptrend ? price + 30 : price - 30;
            this.StopLossPrice = marketDirection == MarketDirection.Uptrend ? price - 30 : price + 30;
            this.CreatedAt = DateTime.Now;
            this.Active = true;
        }

        public override string ToString()
        {
            return $"!!! Trading signal {this.Symbol} {this.MarketDirection} @ {this.Price} $";

        }
    }
}
