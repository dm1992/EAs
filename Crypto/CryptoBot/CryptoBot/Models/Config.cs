using System;
using System.Collections.Generic;

namespace CryptoBot.Models
{
    public class Config
    {
        public string ApiKey { get; set; }
        public string ApiSecret { get; set; }
        public IEnumerable<string> Symbols { get; set; }
        public decimal BuyQuantity { get; set; }
        public decimal SellQuantity { get; set; }
        public decimal BuyLeverage { get; set; }
        public decimal SellLeverage { get; set; }
        public int MarketEntityWindowSize { get; set; }
        public int MarketInformationWindowSize { get; set; }
    }
}
