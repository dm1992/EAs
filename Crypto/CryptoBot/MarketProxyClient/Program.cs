using System;
using System.Threading;
using NLog;
using NLog.Web;
using System.Configuration;
using MarketProxyClient.Providers;
using MarketProxyClient.Interfaces;
using Newtonsoft.Json;
using System.Collections.Generic;
using MarketProxyClient.Managers;
using Common;
using MarketProxy.Socket.BybitSocket;
using MarketClient.Interfaces.Api;
using MarketProxy.BybitApi;
using MarketClient.Interfaces.Socket;

namespace MarketProxyClient
{
    public class Program
    {
        private static readonly string defaultNLog = "_config/nlog.config";
        private static ILogger _logger;

        private static ManualResetEvent _mreStopApplication = new ManualResetEvent(initialState: false);

        public static void Main(string[] args)
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

        private static Config ParseConfiguration()
        {
            try
            {
                Config config = new Config();
                config.Symbols = ConfigurationManager.AppSettings["symbols"].ParseCsv<string>();
                config.MarketEvaluationThresholdConfigs = JsonConvert.DeserializeObject<List<MarketEvaluationThresholdConfig>>(ConfigurationManager.AppSettings["marketEvaluationThresholdConfigs"]);
                config.TradeConfigs = JsonConvert.DeserializeObject<List<TradeConfig>>(ConfigurationManager.AppSettings["tradeConfigs"]);
                config.ReceivedTradesThreshold = int.Parse(ConfigurationManager.AppSettings["receivedTradesThreshold"]);
                config.MarketEvaluationDestinationPath = ConfigurationManager.AppSettings["marketEvaluationDestinationPath"];
                config.ReceivedTradesDestinationPath = ConfigurationManager.AppSettings["receivedTradesDestinationPath"];
                config.CreatedTradesDestinationPath = ConfigurationManager.AppSettings["createdTradesDestinationPath"];
                config.ClosedTradesDestinationPath = ConfigurationManager.AppSettings["closedTradesDestinationPath"];

                return config;
            }
            catch (Exception e)
            {
                _logger.Error($"Failed to parse configuration. {e}");
                throw;
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

                Config config = ParseConfiguration();

                _logger.Info(config.Dump());

                //xxx for now
                ITradeSocket tradeSocket = new BybitTradeSocket(logFactory);
                IPriceSocket priceSocket = new BybitPriceSocket(logFactory);
                IExchangeApi exchangeApi = new BybitExchangeApi(logFactory);

                IMarketEvaluationProvider marketEvaluationProvider = new MarketEvaluationProvider(tradeSocket, config, logFactory);
                marketEvaluationProvider.Initialize();

                IMarketSignalProvider marketSignalProvider = new MarketSignalProvider(marketEvaluationProvider, config, logFactory);
                marketSignalProvider.Initialize();

                ITradingManager tradingManager = new TradingManagerSimulator(exchangeApi, priceSocket, marketSignalProvider, config, logFactory);
                tradingManager.Initialize();

                _mreStopApplication.WaitOne();
            }
            catch (Exception e)
            {
                _logger.Error($"Failed to run application. {e}");
                throw;
            }
        }

        public static void StopApplication()
        {
            _logger.Info("Requested stop application.");

            _mreStopApplication.Set();
        }

      
    }
}
