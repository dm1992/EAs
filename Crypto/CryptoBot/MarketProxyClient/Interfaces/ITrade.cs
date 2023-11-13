using Common;
using MarketProxyClient;
using MarketProxyClient.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MarketProxyClient.Interfaces
{
    public interface ITrade
    {
        string Id { get; set; }
        DateTime CreatedAt { get; }
        DateTime? ClosedAt { get; set; }
        TimeSpan? Duration { get; }
        string Symbol { get; set; }
        MarketDirection MarketDirection { get; set; }
        IMarketEvaluation MarketEvaluation { get; set; }
        decimal TakeProfit { get; set; }
        decimal StopLoss { get; set; }
        decimal OpenPrice { get; set; }
        decimal? ClosePrice { get; set; }
        bool IsActive { get; }
        decimal Balance { get; }

        string Dump();
    }
}
