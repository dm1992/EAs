using MarketProxyClient;
using MarketProxyClient.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CryptoBot.Managers
{
    public class TradingManager : ITradingManager
    {
        // TBD: invoke API through Market Proxy API wrapper

        public bool Initialize()
        {
            throw new NotImplementedException();
        }

        public void SubscribeToMarketSignal()
        {
            throw new NotImplementedException();
        }

        public bool CreateTrade(ITradeArgs tradeArgs, out ITrade trade)
        {
            throw new NotImplementedException();
        }

        public bool CloseTrade(ITradeArgs tradeArgs, out ITrade trade)
        {
            throw new NotImplementedException();
        }

        public decimal GetWalletBalance()
        {
            throw new NotImplementedException();
        }
    }
}
