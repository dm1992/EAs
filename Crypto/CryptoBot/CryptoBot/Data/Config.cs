using System;
using System.Collections.Generic;

namespace CryptoBot.Data
{
    public class Config
    {
        public bool TestMode { get; set; }
        public string Username { get; set; }
        public IEnumerable<string> Symbols { get; set; }
        public string ApiKey { get; set; }
        public string ApiSecret { get; set; }
        public string ApiEndpoint { get; set; }
        public string SpotStreamEndpoint { get; set; }
        public int ActiveSymbolOrders { get; set; }
        public decimal BuyOrderVolume { get; set; }
        public decimal SellOrderVolume { get; set; }
        public int CandlesInTradeBatch { get; set; }
        public int TradeCandleMinuteTimeframe { get; set; }
        public int PriceClosureCandleSize { get; set; }
        public int CreatePriceLevelClosureAfterPriceChanges { get; set; }
        public int MarketPriceClosuresOnMassiveVolumeDetection { get; set; }
        public int MarketPriceClosureCandlesOnMarketDirectionDetection{ get; set; }
        public decimal MassiveBuyersPercentLimit { get; set; }
        public decimal MassiveSellersPercentLimit { get; set; }
        public int AverageVolumeWeightFactor { get; set; }
        public int AveragePriceMoveWeightFactor { get; set; }
    }
}
