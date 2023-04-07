using System;
using System.Collections.Generic;

namespace CryptoBot.Data
{
    public class Config
    {
        public string Username { get; set; }
        public string ApiKey { get; set; }
        public string ApiSecret { get; set; }
        public string ApiEndpoint { get; set; }
        public string SpotStreamEndpoint { get; set; }
        public int ActiveSymbolOrders { get; set; }
        public IEnumerable<string> Symbols { get; set; }
        public int CandlesInBatch { get; set; }
        public int CandleMinuteTimeframe { get; set; }
        public int CreatePriceLevelClosureAfterPriceChanges { get; set; }
        public int MonitorMarketPriceLevels { get; set; }
        public int AverageVolumeWeightFactor { get; set; }
    }
}
