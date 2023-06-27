using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CryptoBot.Models;
using CryptoBot.EventArgs;
using CryptoBot.Interfaces;
using CryptoBot.Managers;
using NLog;
using NLog.Web;
using CryptoBot.Interfaces.Events;
using CryptoBot.Interfaces.Managers;
using CryptoBot.Managers.Production;
using System.Configuration;

namespace CryptoBot
{
    public class Program
    {
        private static readonly string defaultNLog = "_config/nlog.config";
        private static ILogger _logger;

        private static ManualResetEvent _terminateApplication = new ManualResetEvent(initialState: false);

        private static void ApplicationEventHandler(object sender, ApplicationEventArgs e)
        {
            _logger.Debug(e.Dump()); // remove this later!

            // for now only this one is for here
            if (e.EventType == EventType.TerminateApplication)
            {
                _terminateApplication.Set();
            }
        }

        public static void Main(string[] args)
        {
            try
            {
                // set up decimal separator
                System.Globalization.CultureInfo customCulture = (System.Globalization.CultureInfo)Thread.CurrentThread.CurrentCulture.Clone();
                customCulture.NumberFormat.NumberDecimalSeparator = ".";
                Thread.CurrentThread.CurrentCulture = customCulture;

                LogFactory logFactory = NLogBuilder.ConfigureNLog(defaultNLog);
                LogManager.AutoShutdown = false;
                _logger = logFactory.GetCurrentClassLogger();

                SetupManagers(logFactory);

                _terminateApplication.WaitOne();
            }
            catch (Exception e)
            {
                _logger.Error($"Main program exception occurred. {e}");
            }
            finally
            {
                Console.ReadLine();
                Environment.Exit(0);
             
            }
        }

        private static void SetupManagers(LogFactory logFactory)
        {
            try
            {
                Config config = ParseConfiguration();
                if (config == null)
                {
                    _logger.Error("Failed to setup managers.");
                    return;
                }

                IMarketManager marketManager = new MarketManager(logFactory, config);
                marketManager.ApplicationEvent += ApplicationEventHandler;
                marketManager.Initialize();
                marketManager.InvokeWebSocketEventSubscription();

                //xxx add other managers here
            }
            catch (Exception e)
            {
                _logger.Error($"Failed to setup managers. {e}");
            }
        }

        private static Config ParseConfiguration()
        {
            try
            {
                Config config = new Config();

                config.ApiKey = ConfigurationManager.AppSettings["apiKey"];
                config.ApiSecret = ConfigurationManager.AppSettings["apiSecret"];
                config.ApiEndpoint = ConfigurationManager.AppSettings["apiEndpoint"];
                config.SpotStreamEndpoint = ConfigurationManager.AppSettings["spotStreamEndpoint"];
                config.Symbols = ConfigurationManager.AppSettings["symbols"].ParseCsv<string>();
                config.MaxActiveSymbolOrders = int.Parse(ConfigurationManager.AppSettings["maxActiveSymbolOrders"]);
                config.BuyVolume = decimal.Parse(ConfigurationManager.AppSettings["buyVolume"]);
                config.SellVolume = decimal.Parse(ConfigurationManager.AppSettings["sellVolume"]);
                config.MarketEntityWindowSize = int.Parse(ConfigurationManager.AppSettings["marketEntityWindowSize"]);
                config.MarketInformationWindowSize = int.Parse(ConfigurationManager.AppSettings["marketInformationWindowSize"]);
                config.OrderbookDepth = int.Parse(ConfigurationManager.AppSettings["orderbookDepth"]);
                config.Subwindows = int.Parse(ConfigurationManager.AppSettings["subwindows"]);
                config.BuyVolumesPercentageLimit = decimal.Parse(ConfigurationManager.AppSettings["buyVolumesPercentageLimit"]);
                config.SellVolumesPercentageLimit = decimal.Parse(ConfigurationManager.AppSettings["sellVolumesPercentageLimit"]);
                config.UpPriceChangePercentageLimit = decimal.Parse(ConfigurationManager.AppSettings["upPriceChangePercentageLimit"]);
                config.DownPriceChangePercentageLimit = decimal.Parse(ConfigurationManager.AppSettings["downPriceChangePercentageLimit"]);

                return config;
            }
            catch (Exception e)
            {
                _logger.Error($"Failed to parse configuration. {e}");
                return null;
            }
        }
    }
}
