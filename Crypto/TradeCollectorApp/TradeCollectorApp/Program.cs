using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using TradeCollectorApp.Managers;

namespace TradeCollectorApp
{
    public class Program
    {
        private static List<IAPIManager> _apiManagers = new List<IAPIManager>();

        private static void Main(string[] args)
        {
            try
            {
                Console.WriteLine("------------ZAČETEK ZBIRANJA PODATKOV--------------");

                // set up decimal separator
                System.Globalization.CultureInfo customCulture = (System.Globalization.CultureInfo)System.Threading.Thread.CurrentThread.CurrentCulture.Clone();
                customCulture.NumberFormat.NumberDecimalSeparator = ".";
                System.Threading.Thread.CurrentThread.CurrentCulture = customCulture;

                ActivateExchangeAPI();
                WaitForExchangeData();

                Console.WriteLine("------------KONEC ZBIRANJA PODATKOV--------------");
            }
            catch (Exception e)
            {
                Console.WriteLine("Prišlo je do napake: " + e.Message);
            }
            finally
            {
                Console.ReadLine();
            }
        }

        /// <summary>
        /// For now only Bybit exchange is supported.
        /// </summary>
        private static void ActivateExchangeAPI()
        {
            //xxx dummy fix
            foreach (var e in ConfigurationManager.AppSettings["exchanges"].Split(','))
            {
                switch (e)
                {
                    case "bybit":
                        _apiManagers.Add(new BybitAPIManager());
                        break;

                    default:
                        break;
                }
            }
        }

        private static void WaitForExchangeData()
        {
            while (true)
            {
                if (_apiManagers.All(x => x.TradeCollectFinished))
                {
                    // we're done collecting data, exit loop
                    break;
                }
            }
        }
    }
}
