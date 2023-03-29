using System;
using System.Collections.Generic;
using System.Configuration;

namespace CryptoBot.Data
{
    public class Config
    {
        public string ApplicationVersion { get; private set; }
        public string ApplicationLogPath { get; set; }
        public bool ApplicationTestMode { get; private set; }
        public bool SaveDebugApplicationEvent { get; private set; }
        public string Username { get; set; }
        public string ApiKey { get; private set; }
        public string ApiSecret { get; private set; }
        public string ApiEndpoint { get; private set; }
        public string SpotStreamEndpoint { get; private set; }
        public IEnumerable<string> Symbols { get; set; }
        public bool DelayedOrderInvoke { get; set; }
        public int DelayOrderInvokeInMinutes { get; set; }
        public decimal BuyOpenQuantity { get; set; }
        public decimal SellOpenQuantity { get; set; }
        public int ActiveSymbolOrders { get; set; }
        public int TradeLimit { get; set; }
        public decimal AggressiveVolumePercentage { get; set; }
        public decimal PassiveVolumePercentage { get; set; }
        public decimal TotalVolumePercentage { get; set; }

        // MIHA config

        public int CandlesInBatch { get; set; }
        public int CandleMinuteTimeframe { get; set; }
        public int MaxPriceLevelChanges { get; set; }

        public Dictionary<string, decimal> SymbolStopLossAmount { get; private set; }

        public void Parse()
        {
            this.ApplicationVersion = ConfigurationManager.AppSettings["applicationVersion"];
            this.ApplicationLogPath = ConfigurationManager.AppSettings["applicationLogPath"];
            this.ApplicationTestMode = bool.Parse(ConfigurationManager.AppSettings["applicationTestMode"]);
            this.SaveDebugApplicationEvent = bool.Parse(ConfigurationManager.AppSettings["saveDebugApplicationEvent"]);
            this.Username = ConfigurationManager.AppSettings["username"];
            this.ApiKey = ConfigurationManager.AppSettings["apiKey"];
            this.ApiSecret = ConfigurationManager.AppSettings["apiSecret"];
            this.ApiEndpoint = ConfigurationManager.AppSettings["apiEndpoint"];
            this.SpotStreamEndpoint = ConfigurationManager.AppSettings["spotStreamEndpoint"];
            this.Symbols = ConfigurationManager.AppSettings["symbols"].ParseCsv<string>();
            this.DelayedOrderInvoke = bool.Parse(ConfigurationManager.AppSettings["delayedOrderInvoke"]);
            this.DelayOrderInvokeInMinutes = int.Parse(ConfigurationManager.AppSettings["delayOrderInvokeInMinutes"]);
            this.BuyOpenQuantity = decimal.Parse(ConfigurationManager.AppSettings["buyOpenQuantity"]);
            this.SellOpenQuantity = decimal.Parse(ConfigurationManager.AppSettings["sellOpenQuantity"]);
            this.ActiveSymbolOrders = int.Parse(ConfigurationManager.AppSettings["activeSymbolOrders"]);
            this.TradeLimit = int.Parse(ConfigurationManager.AppSettings["tradeLimit"]);
            this.AggressiveVolumePercentage = int.Parse(ConfigurationManager.AppSettings["aggressiveVolumePercentage"]);
            this.PassiveVolumePercentage = int.Parse(ConfigurationManager.AppSettings["passiveVolumePercentage"]);
            this.TotalVolumePercentage = int.Parse(ConfigurationManager.AppSettings["totalVolumePercentage"]);

            // for now
            this.SymbolStopLossAmount = new Dictionary<string, decimal>() { { "BTCUSDT", 40 }, { "SOLUSDT", 5 }, { "LTCUSDT", 2 }, { "ETHUSDT", 10 }, { "BNBUSDT", 10 } };

            this.CandlesInBatch = int.Parse(ConfigurationManager.AppSettings["candlesInBatch"]);
            this.CandleMinuteTimeframe = int.Parse(ConfigurationManager.AppSettings["candleMinuteTimeframe"]);
            this.MaxPriceLevelChanges = int.Parse(ConfigurationManager.AppSettings["maxPriceLevelChanges"]);
        }
    }
}
