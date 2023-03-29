using System;
using System.Collections.Generic;
using System.Configuration;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EA.Data
{
    public class Settings
    {
        public Settings()
        {
            Parse();
        }

        public List<string> TradingSymbolNames { get; private set; } = new List<string>() { "DE30", "US30" };
        //public List<int> GeneralTradingHours { get; private set; } = new List<int>() { 9, 10 };
        //public List<int> SpecialTradingHours { get; private set; } = new List<int>() { 9, 10 };
        public int TakeProfit { get; private set; } = 10000;
        public int StopLoss { get; private set; } = 2500;
        public int BreakEven { get; private set; } = 400;
        public double LotSize { get; private set; } = 0.1;
        public Dictionary<double, double> ClosureRates { get; private set; } = new Dictionary<double, double>()
                                                                            { {10, 1}, {20, 2}, {30, 3}, {40, 4}, {50, 5} };

        internal void Parse()
        {
            try
            {
                TradingSymbolNames = ConfigurationManager.AppSettings["tradingSymbolNames"].Split(',').ToList();
                //GeneralTradingHours = ConfigurationManager.AppSettings["generalTradingHours"].Split(',').Select(x => int.Parse(x)).ToList();
                //SpecialTradingHours = ConfigurationManager.AppSettings["specialTradingHours"].Split(',').Select(x => int.Parse(x)).ToList();
                TakeProfit = int.Parse(ConfigurationManager.AppSettings["takeProfit"]);
                StopLoss = int.Parse(ConfigurationManager.AppSettings["stopLoss"]);
                BreakEven = int.Parse(ConfigurationManager.AppSettings["breakEven"]);
                LotSize = double.Parse(ConfigurationManager.AppSettings["lotSize"], CultureInfo.InvariantCulture);
                ClosureRates = ConfigurationManager.AppSettings["closureRate"].Split(',').ToDictionary(k => double.Parse(k.Split(':').First()), v => double.Parse(v.Split(':').Last()));
            }
            catch (Exception)
            {
                throw;
            }
        }

    }
}
