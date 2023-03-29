using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TradingApp.Data;
using TradingApp.EventArgs;
using TradingApp.Market;

namespace TradingApp
{
    public class Program
    {
        private static ManualResetEvent _exitTradingApp;
        private static Config _config;

        public static void Main(string[] args)
        {
            try
            {
                // set up decimal separator
                System.Globalization.CultureInfo customCulture = (System.Globalization.CultureInfo)Thread.CurrentThread.CurrentCulture.Clone();
                customCulture.NumberFormat.NumberDecimalSeparator = ".";
                Thread.CurrentThread.CurrentCulture = customCulture;

                _exitTradingApp = new ManualResetEvent(initialState: false);
                _config = new Config();

                Console.WriteLine(_config.ToString());

                var marketWatcher = new MarketWatcher(_config);
                marketWatcher.ApplicationEvent += ApplicationEventHandler;

                var candleAnalyzer = new CandleAnalyzer(marketWatcher, _config);
                candleAnalyzer.ApplicationEvent += ApplicationEventHandler;

                // skip trading in case TradingMode is set
                if (_config.TradingMode)
                {
                    var tradingWorker = new TradingWorker(candleAnalyzer, _config);
                    tradingWorker.ApplicationEvent += ApplicationEventHandler;
                }

                _exitTradingApp.WaitOne();
            }

            catch (Exception e)
            {
                Console.WriteLine("Error occurred: " + e.Message);
            }
            finally
            {
                Console.ReadLine();

                Environment.Exit(0);
            }
        }

        private static void ApplicationEventHandler(object sender, ApplicationEventArgs args)
        {
            var outputMsg = $"[{sender} -> {args}\n";

            Console.Write(outputMsg);

            if (!Helpers.SaveData(outputMsg, Path.Combine(_config.ApplicationEventDataDirectory, $"applicationData_{DateTime.Now:ddMMyyyy}.txt"), out string errorReason))
            {
                Console.WriteLine($"Failed to save data. Reason: {errorReason}");
            }

            if (args.Type == EventType.STOP_TRADING)
            {
                _exitTradingApp.Set();
                return;
            }
        }
    }
}
