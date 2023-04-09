using CryptoBot.Data;
using CryptoBot.EventArgs;
using CryptoBot.Interfaces;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CryptoBot.Managers
{
    public static class ApplicationHandler
    {
        private static Config _config = null;
        private static bool _isInitialized = false;

        public static bool Initialize()
        {
            try
            {
                if (_isInitialized) return true;

                // set up decimal separator
                System.Globalization.CultureInfo customCulture = (System.Globalization.CultureInfo)Thread.CurrentThread.CurrentCulture.Clone();
                customCulture.NumberFormat.NumberDecimalSeparator = ".";
                Thread.CurrentThread.CurrentCulture = customCulture;

                if (!SetupApplicationConfiguration())
                    return false;

                if (!SetupApplicationManagers())
                    return false;

                SaveApplicationMessage($"Running application with username '{_config.Username}'.\n");

                _isInitialized = true;
                return true;
            }
            catch (Exception e)
            {
                SaveApplicationMessage($"!!!Failed to initialize application!!! {e}");

                return false;
            }
        }

        private static bool SetupApplicationConfiguration()
        {
            try
            {
                _config = new Config();

                _config.TestMode = bool.Parse(ConfigurationManager.AppSettings["testMode"]);
                _config.Username = ConfigurationManager.AppSettings["username"];
                _config.ApiKey = ConfigurationManager.AppSettings["apiKey"];
                _config.ApiSecret = ConfigurationManager.AppSettings["apiSecret"];
                _config.ApiEndpoint = ConfigurationManager.AppSettings["apiEndpoint"];
                _config.SpotStreamEndpoint = ConfigurationManager.AppSettings["spotStreamEndpoint"];
                _config.Symbols = ConfigurationManager.AppSettings["symbols"].ParseCsv<string>();
                _config.BuyOpenQuantity = decimal.Parse(ConfigurationManager.AppSettings["buyOpenQuantity"]);
                _config.SellOpenQuantity = decimal.Parse(ConfigurationManager.AppSettings["sellOpenQuantity"]);
                _config.ActiveSymbolOrders = int.Parse(ConfigurationManager.AppSettings["activeSymbolOrders"]);
                _config.CandlesInBatch = int.Parse(ConfigurationManager.AppSettings["candlesInBatch"]);
                _config.CandleMinuteTimeframe = int.Parse(ConfigurationManager.AppSettings["candleMinuteTimeframe"]);
                _config.CreatePriceLevelClosureAfterPriceChanges = int.Parse(ConfigurationManager.AppSettings["createPriceLevelClosureAfterPriceChanges"]);
                _config.MonitorMarketPriceLevels = int.Parse(ConfigurationManager.AppSettings["monitorMarketPriceLevels"]);
                _config.AverageVolumeWeightFactor = int.Parse(ConfigurationManager.AppSettings["averageVolumeWeightFactor"]);

                return true;
            }
            catch (Exception e)
            {
                SaveApplicationMessage($"!!!Failed to setup application configuration!!! {e}");

                return false;
            }
        }

        private static bool SetupApplicationManagers()
        {
            try
            {

                ITradingAPIManager tradingManager = new TradingAPIManager(_config);
                tradingManager.ApplicationEvent += ApplicationEventHandler;

                ManagerType managerType = (ManagerType)Enum.Parse(typeof(ManagerType), _config.Username);

                IMarketManager marketManager = ManagerFactory.CreateMarketManager(managerType, tradingManager, _config);
                if (marketManager != null)
                {
                    marketManager.ApplicationEvent += ApplicationEventHandler;
                    marketManager.Initialize();
                    marketManager.InvokeAPISubscription();
                }

                IOrderManager orderManager = ManagerFactory.CreateOrderManager(managerType, tradingManager, marketManager, _config);
                if (orderManager != null)
                {
                    orderManager.ApplicationEvent += ApplicationEventHandler;
                    orderManager.Initialize();
                    orderManager.InvokeAPISubscription();
                }

                return true;
            }
            catch (Exception e)
            {
                SaveApplicationMessage($"!!!Failed to setup application managers!!! {e}");

                return false;
            }
        }

        private static void SaveApplicationMessage(string message, string messageScope = null)
        {
            Program.OutputData(message, messageScope);
        }

        private static void TerminateApplication()
        {
            Program.Terminate();
        }

        private static void ApplicationEventHandler(object sender, ApplicationEventArgs args)
        {
            SaveApplicationMessage(args.ToString(), args.MessageScope);

            if (args.Type == EventType.TerminateApplication)
            {
                TerminateApplication();
            }
        }
    }
}
