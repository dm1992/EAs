using CryptoBot.Data;
using CryptoBot.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CryptoBot.Managers
{
    public class ManagerFactory
    {
        public static IMarketManager CreateMarketManager(ManagerType type, Config config, bool testMode)
        {
            switch (type)
            {
                case ManagerType.Davor:
                    return new Davor_old.MarketManager(config);
                case ManagerType.Miha:
                    return new Miha.MarketManager(config);

                default:
                    return null;
            }
        }
    }
}
