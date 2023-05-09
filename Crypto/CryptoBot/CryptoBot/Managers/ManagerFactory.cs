using CryptoBot.Models;
using CryptoBot.Interfaces.Managers;
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
        public static IMarketManager CreateMarketManager(ManagerType type, ITradingManager tradingManager, IOrderManager orderManager, AppConfig config)
        {
            switch (type)
            {
                //xxx add other manager types
                case ManagerType.Davor:
                    return new MarketManager(tradingManager, orderManager, config);

                default:
                    return null;
            }
        }

        public static IOrderManager CreateOrderManager(ManagerType type, ITradingManager tradingManager, AppConfig config)
        {
            switch (type)
            {
                //xxx add other manager types
                case ManagerType.Davor:
                    return new OrderManager(tradingManager, config);

                default:
                    return null;
            }
        }

        public static ITradingManager CreateTradingManager(ManagerType type, AppConfig config)
        {
            switch (type)
            {
                //xxx add other manager types
                case ManagerType.Davor:
                {
                    if (config.TestMode)
                    {
                        return new Test.TradingManager(config);
                    }

                    return new TradingManager(config);
                }
                default:
                    return null;
            }
        }
    }
}
