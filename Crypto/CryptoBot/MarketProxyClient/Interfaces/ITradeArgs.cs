using Common;
using MarketProxyClient;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MarketProxyClient.Interfaces
{
    public interface ITradeArgs
    {
        string Id { get; }
        string Symbol { get; set; }
        MarketDirection MarketDirection { get; set; }
        IMarketEvaluation MarketEvaluation { get; set; }
        decimal Price { get; set; }
        decimal TakeProfitPrice { get; set; }
        decimal StopLossPrice { get; set; }
    }
}
