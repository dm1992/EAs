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

        public int OpenHour { get; private set; } = 10;
        public int CloseHour { get; private set; } = 17;
        public int TakeProfit { get; private set; } = 500;
        public int StopLoss { get; private set; } = 1600;
        public int BreakEven { get; private set; } = 340;
        public int CandleSet { get; private set; } = 9;
        public int CandleColorChangesLimit { get; private set; } = 4;
        public int LastCandlePipSize { get; private set; } = 50;
        public int Period { get; set; } = 60;
        public double LotSize { get; private set; } = 0.1;
        public bool LimitTradePerDay { get; private set; } = true;
        public int ExtremeDiff { get; set; } = 1;

        internal void Parse()
        {
            try
            {
                OpenHour = int.Parse(ConfigurationManager.AppSettings["openHour"]);
                CloseHour = int.Parse(ConfigurationManager.AppSettings["closeHour"]);
                TakeProfit = int.Parse(ConfigurationManager.AppSettings["takeProfit"]);
                StopLoss = int.Parse(ConfigurationManager.AppSettings["stopLoss"]);
                BreakEven = int.Parse(ConfigurationManager.AppSettings["breakEven"]);
                CandleSet = int.Parse(ConfigurationManager.AppSettings["candleSet"]);
                CandleColorChangesLimit = int.Parse(ConfigurationManager.AppSettings["candleColorChangesLimit"]);
                LastCandlePipSize = int.Parse(ConfigurationManager.AppSettings["lastCandlePipSize"]);
                Period = int.Parse(ConfigurationManager.AppSettings["period"]);
                LotSize = double.Parse(ConfigurationManager.AppSettings["lotSize"], CultureInfo.InvariantCulture);
                LimitTradePerDay = bool.Parse(ConfigurationManager.AppSettings["limitTradePerDay"]);
                ExtremeDiff = int.Parse(ConfigurationManager.AppSettings["extremeDiff"]);
            }
            catch (Exception)
            {
                throw;
            }
        }

    }
}
