using Common;
using DataAnalyzer.Managers;
using MarketAnalyzer.Configs;
using Newtonsoft.Json;
using NLog;
using NLog.Web;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace DataAnalyzer
{
    public class Program
    {
        private static readonly string defaultNLog = "_config/nlog.config";
        private static ILogger _logger;

        private static void Main(string[] args)
        {
            try
            {
                RunApplication();
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

        private static bool DeserializeConfigJson(out BacktestConfig config)
        {
            config = null;

            try
            {
                string configFilePath = ConfigurationManager.AppSettings["configJsonFilePath"];
                string content = Helpers.ReadAllTextFromFile(configFilePath);

                config = JsonConvert.DeserializeObject<BacktestConfig>(content);
                return true;
            }
            catch (Exception e)
            {
                _logger.Error($"Failed to deserialize config json. {e}");
                return false;
            }
        }

        private static void RunApplication()
        {
            try
            {
                // set up decimal separator
                System.Globalization.CultureInfo customCulture = (System.Globalization.CultureInfo)Thread.CurrentThread.CurrentCulture.Clone();
                customCulture.NumberFormat.NumberDecimalSeparator = ",";
                Thread.CurrentThread.CurrentCulture = customCulture;

                LogFactory logFactory = NLogBuilder.ConfigureNLog(defaultNLog);
                LogManager.AutoShutdown = false;
                _logger = logFactory.GetCurrentClassLogger();

                if (!DeserializeConfigJson(out BacktestConfig config))
                {
                    _logger.Error("Failed to run application. Unable to deserialize config json.");
                    return;
                }

                BacktestManager backtestManager = new BacktestManager(config, logFactory);
                
                if (!backtestManager.Initialize())
                {
                    _logger.Error("Failed to run application. Unable to initialize backtest manager.");
                    return;
                }

                backtestManager.ExecuteBacktest();
            }
            catch (Exception e)
            {
                _logger.Error($"Failed to run application. {e}");
                throw;
            }
        }
    }
}
