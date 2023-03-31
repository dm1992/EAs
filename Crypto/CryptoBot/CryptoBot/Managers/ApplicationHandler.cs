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
        public static Config _config = null;

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

                SaveApplicationMessage($"Initialized application version '{_config.ApplicationVersion}' with username '{_config.Username}'.\n");

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

                _config.ApplicationVersion = ConfigurationManager.AppSettings["applicationVersion"];
                _config.ApplicationLogPath = ConfigurationManager.AppSettings["applicationLogPath"];
                _config.ApplicationTestMode = bool.Parse(ConfigurationManager.AppSettings["applicationTestMode"]);
                _config.Username = ConfigurationManager.AppSettings["username"];
                _config.ApiKey = ConfigurationManager.AppSettings["apiKey"];
                _config.ApiSecret = ConfigurationManager.AppSettings["apiSecret"];
                _config.ApiEndpoint = ConfigurationManager.AppSettings["apiEndpoint"];
                _config.SpotStreamEndpoint = ConfigurationManager.AppSettings["spotStreamEndpoint"];
                _config.Symbols = ConfigurationManager.AppSettings["symbols"].ParseCsv<string>();
                _config.DelayedOrderInvoke = bool.Parse(ConfigurationManager.AppSettings["delayedOrderInvoke"]);
                _config.DelayOrderInvokeInMinutes = int.Parse(ConfigurationManager.AppSettings["delayOrderInvokeInMinutes"]);
                _config.BuyOpenQuantity = decimal.Parse(ConfigurationManager.AppSettings["buyOpenQuantity"]);
                _config.SellOpenQuantity = decimal.Parse(ConfigurationManager.AppSettings["sellOpenQuantity"]);
                _config.ActiveSymbolOrders = int.Parse(ConfigurationManager.AppSettings["activeSymbolOrders"]);
                _config.TradeLimit = int.Parse(ConfigurationManager.AppSettings["tradeLimit"]);
                _config.AggressiveVolumePercentage = int.Parse(ConfigurationManager.AppSettings["aggressiveVolumePercentage"]);
                _config.PassiveVolumePercentage = int.Parse(ConfigurationManager.AppSettings["passiveVolumePercentage"]);
                _config.TotalVolumePercentage = int.Parse(ConfigurationManager.AppSettings["totalVolumePercentage"]);

                // for now
                _config.SymbolStopLossAmount = new Dictionary<string, decimal>() { { "BTCUSDT", 40 }, { "SOLUSDT", 5 }, { "LTCUSDT", 2 }, { "ETHUSDT", 10 }, { "BNBUSDT", 10 } };

                _config.CandlesInBatch = int.Parse(ConfigurationManager.AppSettings["candlesInBatch"]);
                _config.CandleMinuteTimeframe = int.Parse(ConfigurationManager.AppSettings["candleMinuteTimeframe"]);
                _config.PriceLevelChanges = int.Parse(ConfigurationManager.AppSettings["priceLevelChanges"]);

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
                ManagerType managerType = (ManagerType)Enum.Parse(typeof(ManagerType), _config.Username);

                IMarketManager marketManager = ManagerFactory.CreateMarketManager(managerType, _config, _config.ApplicationTestMode); //xxx for now only here
                if (marketManager != null)
                {
                    marketManager.ApplicationEvent += ApplicationEventHandler;
                    marketManager.Initialize();
                    marketManager.InvokeAPISubscription();
                }

                //ITradingManager tradingManager = new TradingManager(_config);
                //tradingManager.ApplicationEvent += ApplicationEventHandler;

                //IOrderManager orderManager = new OrderManager(tradingManager, marketManager, _config);
                //orderManager.ApplicationEvent += ApplicationEventHandler;
                //orderManager.InvokeAPISubscription();

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

        private static void ApplicationEventHandler(object sender, ApplicationEventArgs args)
        {
            SaveApplicationMessage(args.ToString(), args.MessageScope);

            if (args.Type == EventType.TerminateApplication)
            {
                Program.Terminate();
            }
        }

        
    }
}
