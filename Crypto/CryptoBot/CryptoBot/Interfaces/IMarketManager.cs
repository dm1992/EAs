using CryptoBot.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CryptoBot.Interfaces
{
    public interface IMarketManager: IAPISubscription
    {
        void Initialize();
        bool GetCurrentMarket(string symbol, out IMarket market);
    }
}
