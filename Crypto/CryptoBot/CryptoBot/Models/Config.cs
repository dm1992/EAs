using System;
using System.Collections.Generic;

namespace CryptoBot.Models
{
    public class Config
    {
        public string ApiKey { get; set; }
        public string ApiSecret { get; set; }
        public string ApiEndpoint { get; set; }
        public string SpotStreamEndpoint { get; set; }
        public IEnumerable<string> Symbols { get; set; }
        public int MaxActiveSymbolOrders { get; set; }
        public decimal BuyVolume { get; set; }
        public decimal SellVolume { get; set; }
        public int MarketEntityWindowSize { get; set; }
        public int MarketInformationWindowSize { get; set; }
        public int? OrderbookDepth { get; set; }
        public int? Subwindows { get; set; }
        public decimal BuyVolumesPercentageLimit { get; set; } 
        public decimal SellVolumesPercentageLimit { get; set; }
        public decimal UpPriceChangePercentageLimit { get; set; } 
        public decimal DownPriceChangePercentageLimit { get; set; }
    }
}
