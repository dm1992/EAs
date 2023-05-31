using CryptoBot.Models;
using CryptoBot.Interfaces.Events;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CryptoBot.Interfaces.Managers
{
    public interface IMarketManager : IManager, IWebSocketEvent
    {
        MarketDirection GetMarketDirection(string symbol);
    }
}
