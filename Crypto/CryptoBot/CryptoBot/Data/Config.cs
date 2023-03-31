using System;
using System.Collections.Generic;

namespace CryptoBot.Data
{
    public class Config
    {
        public string ApplicationVersion { get; set; }
        public string ApplicationLogPath { get; set; }
        public bool ApplicationTestMode { get; set; }
        public bool SaveDebugApplicationEvent { get; set; }
        public string Username { get; set; }
        public string ApiKey { get; set; }
        public string ApiSecret { get; set; }
        public string ApiEndpoint { get; set; }
        public string SpotStreamEndpoint { get; set; }
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
        public int PriceLevelChanges { get; set; }

        public Dictionary<string, decimal> SymbolStopLossAmount { get; set; }

    }
}
