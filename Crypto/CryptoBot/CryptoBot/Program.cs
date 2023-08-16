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

                MarketManager marketManager = new MarketManager(logFactory, config);

                if (!marketManager.Initialize())
                {
                    _logger.Error("Failed to initialize market manager.");
                    return;
                }

                marketManager.InvokeWebSocketSubscription();
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
                config.Symbols = ConfigurationManager.AppSettings["symbols"].ParseCsv<string>();
                config.MarketEvaluationWindowSize = int.Parse(ConfigurationManager.AppSettings["marketEvaluationWindowSize"]);
                config.MarketConfirmationWindowSize = int.Parse(ConfigurationManager.AppSettings["marketConfirmationWindowSize"]);

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
