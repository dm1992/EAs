using MarketProxyClient;
using MarketProxyClient.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MarketProxyClient.Interfaces
{
    public interface ITradingManager : IMarketProxyClient
    {
        bool CreateTrade(ITradeArgs tradeArgs, out ITrade tradeInfo);

        bool CloseTrade(ITradeArgs tradeArgs, out ITrade tradeInfo);

        decimal GetWalletBalance();
    }
}
