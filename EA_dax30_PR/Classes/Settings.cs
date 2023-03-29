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
        public int OpenMinute { get; private set; } = 10;
        public int CloseHour { get; private set; } = 23;
        public int TakeProfit1 { get; private set; } = 9650;
        public int StopLoss { get; private set; } = 9600;
        public int BreakEven { get; private set; } = 9340;
        public int TrendPeriod { get; private set; } = 13;
        public double LotSize { get; private set; } = 1;
        public double TailSize { get; private set; } = 2;
        public double OrderProfitWeight { get; private set; } = 0.2;
        public double OrderProfitPercent { get; private set; } = 1;
        public double TotalProfitPercent { get; private set; } = 5;
        public double TotalLossPercent { get; private set; } = 50;
        public double TotalProfitPercentAux { get; private set; } = 1;
        public int TotalProfitPercentResetHour { get; private set; } = 13;
        public List<int> ReportingHours { get; private set; } = new List<int>() { 10, 11, 12, 13, 14, 15, 16, 17, 18 };
        public Dictionary<double, double> ClosureRates { get; private set; } = new Dictionary<double, double>()
                                                                            { {500, 20}, {1000, 20}, {1500, 20}, {2000, 20}, {2500, 20} };
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
                LotSize = double.Parse(ConfigurationManager.AppSettings["lotSize"], CultureInfo.InvariantCulture);
                TailSize = double.Parse(ConfigurationManager.AppSettings["tailSize"], CultureInfo.InvariantCulture);
                OrderProfitWeight = double.Parse(ConfigurationManager.AppSettings["orderProfitWeight"], CultureInfo.InvariantCulture);
                OrderProfitPercent = double.Parse(ConfigurationManager.AppSettings["orderProfitPercent"], CultureInfo.InvariantCulture);
                TotalProfitPercent = double.Parse(ConfigurationManager.AppSettings["totalProfitPercent"], CultureInfo.InvariantCulture);
                TotalLossPercent = double.Parse(ConfigurationManager.AppSettings["totalLossPercent"], CultureInfo.InvariantCulture);
                TotalProfitPercentAux = double.Parse(ConfigurationManager.AppSettings["totalProfitPercentAux"], CultureInfo.InvariantCulture);
                TotalProfitPercentResetHour = int.Parse(ConfigurationManager.AppSettings["totalProfitPercentResetHour"]);

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
