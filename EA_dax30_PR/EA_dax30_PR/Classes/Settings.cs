using System;
using System.Collections.Generic;
using System.Configuration;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EATester.Classes
{
    public class Settings
    {
        public int OpenHour { get; private set; } = 10;
        public int OpenMinute { get; private set; } = 15;
        public int CloseHour { get; private set; } = 23;
        public int TakeProfit1 { get; private set; } = 9650;
        public int StopLoss { get; private set; } = 9600;
        public int BreakEven { get; private set; } = 9340;
        public int TrendPeriod { get; private set; } = 13;
        public int MaxDayLosses { get; private set; } = 2;
        public double LotSize { get; private set; } = 0.4;
        public double TailSize { get; private set; } = 2;
        public double ProfitPercent { get; private set; } = 15;
        public double LossPercent { get; private set; } = 50;
        public bool UseRegularTradeGrowth { get; private set; } = true;
        public double TradeGrowthPercent { get; private set; } = 1;
        public double TradeGrowthPips { get; private set; } = 4.5;
        public List<int> ReportingHours { get; private set; } = new List<int>() { 10, 11, 12, 13, 14, 15, 16, 17, 18 };
        public void Parse()
        {
            try
            {
                OpenHour = int.Parse(ConfigurationManager.AppSettings["openHour"]);
                OpenMinute = int.Parse(ConfigurationManager.AppSettings["openMinute"]);
                CloseHour = int.Parse(ConfigurationManager.AppSettings["closeHour"]);
                TakeProfit1 = int.Parse(ConfigurationManager.AppSettings["takeProfit1"]);
                StopLoss = int.Parse(ConfigurationManager.AppSettings["stopLoss"]);
                BreakEven = int.Parse(ConfigurationManager.AppSettings["breakEven"]);
                TrendPeriod = int.Parse(ConfigurationManager.AppSettings["trendPeriod"]);
                MaxDayLosses = int.Parse(ConfigurationManager.AppSettings["maxDayLosses"]);
                LotSize = double.Parse(ConfigurationManager.AppSettings["lotSize"], CultureInfo.InvariantCulture);
                TailSize = double.Parse(ConfigurationManager.AppSettings["tailSize"], CultureInfo.InvariantCulture);
                ProfitPercent = double.Parse(ConfigurationManager.AppSettings["profitPercent"], CultureInfo.InvariantCulture);
                LossPercent = double.Parse(ConfigurationManager.AppSettings["lossPercent"], CultureInfo.InvariantCulture);
                UseRegularTradeGrowth = bool.Parse(ConfigurationManager.AppSettings["useRegularTradeGrowth"]);
                TradeGrowthPercent = double.Parse(ConfigurationManager.AppSettings["tradeGrowthPercent"], CultureInfo.InvariantCulture);
                TradeGrowthPips = double.Parse(ConfigurationManager.AppSettings["tradeGrowthPips"], CultureInfo.InvariantCulture);

                string [] reportingHours = ConfigurationManager.AppSettings["reportingHours"].Split(',');
                ReportingHours.Clear();

                foreach (string hour in reportingHours)
                {
                    ReportingHours.Add(int.Parse(hour));
                }
            }
            catch (Exception)
            {
                throw;
            }
        }
    }
}
