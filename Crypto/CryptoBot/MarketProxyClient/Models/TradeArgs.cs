using Common;
using MarketProxyClient;
using MarketProxyClient.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MarketProxyClient.Models
{
    public class TradeArgs : ITradeArgs
    {
        public string Id { get; set; }
        public string Symbol { get; set; }
        public MarketDirection MarketDirection { get; set; }
        public IMarketEvaluation MarketEvaluation { get; set; }
        public decimal Price { get; set; }
        public decimal TakeProfitPrice { get; set; }
        public decimal StopLossPrice { get; set; }

        public TradeArgs(string symbol)
        {
            this.Id = Guid.NewGuid().ToString();
            this.Symbol = symbol;
        }

        public TradeArgs(string id, string symbol)
        {
            this.Id = id;
            this.Symbol = symbol;
        }
    }
}
