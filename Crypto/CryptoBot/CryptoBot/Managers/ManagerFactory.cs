﻿using CryptoBot.Data;
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
        public static IMarketManager CreateMarketManager(ManagerType type, ITradingAPIManager tradingManager, Config config)
        {
            switch (type)
            {
                //case ManagerType.Davor:
                //    return new Davor_old.MarketManager(tradingManager, config);
                case ManagerType.Miha:
                    return new Miha.MarketManager(tradingManager, config);

                default:
                    return null;
            }
        }

        public static IOrderManager CreateOrderManager(ManagerType type, ITradingAPIManager tradingManager, IMarketManager marketManager, Config config)
        {
            switch (type)
            {
                //case ManagerType.Davor:
                //    return new Davor_old.OrderManager(tradingManager, marketManager, config);
                case ManagerType.Miha:
                    return new Miha.OrderManager(tradingManager, marketManager, config);

                default:
                    return null;
            }
        }
    }
}
