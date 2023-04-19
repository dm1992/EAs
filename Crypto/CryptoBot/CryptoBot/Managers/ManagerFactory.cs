using CryptoBot.Data;
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
        public static IMarketManager CreateMarketManager(ManagerType type, ITradingManager tradingManager, IOrderManager orderManager, Config config)
        {
            switch (type)
            {
                //xxx add other manager types
                case ManagerType.Miha:
                    return new Miha.MarketManager(tradingManager, orderManager, config);

                default:
                    return null;
            }
        }

        public static IOrderManager CreateOrderManager(ManagerType type, ITradingManager tradingManager, Config config)
        {
            switch (type)
            {
                //xxx add other manager types
                case ManagerType.Miha:
                    return new Miha.OrderManager(tradingManager, config);

                default:
                    return null;
            }
        }

        public static ITradingManager CreateTradingManager(ManagerType type, Config config)
        {
            switch (type)
            {
                //xxx add other manager types
                case ManagerType.Miha:
                {
                    if (config.TestMode)
                    {
                        return new Test.TradingManager(config);
                    }

                    return new Miha.TradingManager(config);
                }
                default:
                    return null;
            }
        }
    }
}
