using CryptoBot.Models;
using CryptoBot.EventArgs;
using CryptoBot.Interfaces.Managers;
using System;
using System.Configuration;
using System.IO;
using System.Threading;

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

                string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, Program.BASE_APPLICATION_DIRECTORY);

                if (!Helpers.DeleteDirectoryFiles(path))
                    throw new Exception($"Failed to delete application directory {path}.");

                if (!SetupApplicationConfiguration())
                    return false;

                if (!SetupApplicationManagers())
                    return false;

                SaveApplicationMessage($"Running application with username '{_config.Username}'.\nSaving results to application directory '{path}'.\n");

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
                _config.TestBalance = decimal.Parse(ConfigurationManager.AppSettings["testBalance"]);
                _config.BalanceProfitPercent = decimal.Parse(ConfigurationManager.AppSettings["balanceProfitPercent"]);
                _config.BalanceLossPercent = decimal.Parse(ConfigurationManager.AppSettings["balanceLossPercent"]);
                _config.OrderProfitPercent = decimal.Parse(ConfigurationManager.AppSettings["orderProfitPercent"]);
                _config.OrderLossPercent = decimal.Parse(ConfigurationManager.AppSettings["orderLossPercent"]);
                _config.Username = ConfigurationManager.AppSettings["username"];
                _config.Symbols = ConfigurationManager.AppSettings["symbols"].ParseCsv<string>();
                _config.ApiKey = ConfigurationManager.AppSettings["apiKey"];
                _config.ApiSecret = ConfigurationManager.AppSettings["apiSecret"];
                _config.ApiEndpoint = ConfigurationManager.AppSettings["apiEndpoint"];
                _config.SpotStreamEndpoint = ConfigurationManager.AppSettings["spotStreamEndpoint"];
                _config.ActiveSymbolOrders = int.Parse(ConfigurationManager.AppSettings["activeSymbolOrders"]);
                _config.BuyOrderVolume = decimal.Parse(ConfigurationManager.AppSettings["buyOrderVolume"]);
                _config.SellOrderVolume = decimal.Parse(ConfigurationManager.AppSettings["sellOrderVolume"]);
                _config.CandlesInTradeBatch = int.Parse(ConfigurationManager.AppSettings["candlesInTradeBatch"]);
                _config.TradeCandleMinuteTimeframe = int.Parse(ConfigurationManager.AppSettings["tradeCandleMinuteTimeframe"]);
                _config.PriceClosureCandleSize = int.Parse(ConfigurationManager.AppSettings["priceClosureCandleSize"]);
                _config.CreatePriceLevelClosureAfterPriceChanges = int.Parse(ConfigurationManager.AppSettings["createPriceLevelClosureAfterPriceChanges"]);
                _config.MarketPriceClosuresOnMassiveVolumeDetection = int.Parse(ConfigurationManager.AppSettings["marketPriceClosuresOnMassiveVolumeDetection"]);
                _config.MarketPriceClosureCandlesOnMarketDirectionDetection = int.Parse(ConfigurationManager.AppSettings["marketPriceClosureCandlesOnMarketDirectionDetection"]);
                _config.MassiveBuyersPercentLimit = decimal.Parse(ConfigurationManager.AppSettings["massiveBuyersPercentLimit"]);
                _config.MassiveSellersPercentLimit = decimal.Parse(ConfigurationManager.AppSettings["massiveSellersPercentLimit"]);
                _config.AverageVolumeWeightFactor = decimal.Parse(ConfigurationManager.AppSettings["averageVolumeWeightFactor"]);
                _config.AveragePriceMoveWeightFactor = decimal.Parse(ConfigurationManager.AppSettings["averagePriceMoveWeightFactor"]);

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

                ITradingManager tradingManager = ManagerFactory.CreateTradingManager(managerType, _config);
                if (tradingManager == null)
                {
                    SaveApplicationMessage($"!!!Failed to create trading manager!!!");
                    return false;
                }

                IOrderManager orderManager = ManagerFactory.CreateOrderManager(managerType, tradingManager, _config);
                if (orderManager == null)
                {
                    SaveApplicationMessage($"!!!Failed to create order manager!!!");
                    return false;
                }

                IMarketManager marketManager = ManagerFactory.CreateMarketManager(managerType, tradingManager, orderManager, _config);
                if (marketManager == null)
                {
                    SaveApplicationMessage($"!!!Failed to create market manager!!!");
                    return false;
                }

                // listen events from managers
                marketManager.ApplicationEvent += ApplicationEventHandler;
                orderManager.ApplicationEvent += ApplicationEventHandler;
                tradingManager.ApplicationEvent += ApplicationEventHandler;

                tradingManager.Initialize();
                orderManager.Initialize();
                marketManager.Initialize();

                marketManager.InvokeWebSocketEventSubscription();
                orderManager.InvokeWebSocketEventSubscription();

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
            string messageScope = null;
            if (!args.MessageScope.IsNullOrEmpty())
            {
                messageScope = $"{args.EventTag}_{args.MessageScope}";
            }

            SaveApplicationMessage(args.Dump(), messageScope);

            if (args.EventType == EventType.TerminateApplication)
            {
                TerminateApplication();
            }
        }

        private static void TerminateApplication()
        {
            Program.TerminateApplication();
        }
    }
}
