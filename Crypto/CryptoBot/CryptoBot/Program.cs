using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CryptoBot.Data;
using CryptoBot.EventArgs;
using CryptoBot.Interfaces;
using CryptoBot.Managers;
using CryptoBot.Managers.Davor;

namespace CryptoBot
{
    public class Program
    {
        private static ManualResetEvent _terminateApplication = new ManualResetEvent(initialState: false);
        private static Config _config = new Config();

        public static void Main(string[] args)
        {
            try
            {
                // set up decimal separator
                System.Globalization.CultureInfo customCulture = (System.Globalization.CultureInfo)Thread.CurrentThread.CurrentCulture.Clone();
                customCulture.NumberFormat.NumberDecimalSeparator = ".";
                Thread.CurrentThread.CurrentCulture = customCulture;

                _config.Parse();

                SaveMessage($"Started application version '{_config.ApplicationVersion}' with username '{_config.Username}'.\n");

                if (!Enum.TryParse(_config.Username, ignoreCase: true, out ManagerType managerType))
                {
                    SaveMessage($"!!!Manager type failed to parse from username '{_config.Username}'. Can't use application.");
                    return;
                }

                SetupManager(managerType);

                _terminateApplication.WaitOne();
            }
            catch (Exception e)
            {
                SaveMessage($"!!!Error occurred!!! {e}");
            }
            finally
            {
                Console.ReadLine();
                Environment.Exit(0);
             
            }
        }

        private static void SetupManager(ManagerType managerType)
        {
            IMarketManager marketManager = ManagerFactory.CreateMarketManager(managerType, _config, _config.ApplicationTestMode); //xxx for now only here
            if (marketManager != null)
            {
                marketManager.ApplicationEvent += EventHandler;
                marketManager.Initialize();
                marketManager.InvokeAPISubscription();
            }

            //ITradingManager tradingManager = new TradingManager(_config);
            //tradingManager.ApplicationEvent += ApplicationEventHandler;

            //IOrderManager orderManager = new OrderManager(tradingManager, marketManager, _config);
            //orderManager.ApplicationEvent += ApplicationEventHandler;
            //orderManager.InvokeAPISubscription();
        }

        private static void EventHandler(object sender, ApplicationEventArgs args)
        {
            bool shouldSaveMessage = true;
            try
            {
                if (args.Type == EventType.TerminateApplication)
                {
                    _terminateApplication.Set();
                }
                else if (args.Type == EventType.Debug)
                {
                    shouldSaveMessage = _config.SaveDebugApplicationEvent;
                }
            }
            finally
            {
                if (shouldSaveMessage)
                {
                    SaveMessage(args.ToString(), args.MessageScope);
                }
            }
        }

        private static void SaveMessage(string message, string messageScope = null)
        {
            if (String.IsNullOrEmpty(message)) return;

            if (String.IsNullOrEmpty(messageScope))
            {
                // only general data is output to console
                Console.Write(message);
            }

            string path = Path.Combine(_config.ApplicationLogPath, 
                          $"{messageScope ?? "general"}_applicationData_{DateTime.Now:ddMMyyyy}_{_config.ApplicationVersion}.txt");

            if (!Helpers.SaveToFile(message, path))
            {
                Console.WriteLine($"!!!Failed to save application message '{message}'!!!");
            }
        }
    }
}
