using System;
using System.Collections.Generic;

namespace CryptoBot.Models
{
    public class Config
    {
        public string ApiKey { get; set; }
        public string ApiSecret { get; set; }
        public IEnumerable<string> Symbols { get; set; }
        public int MarketEvaluationWindowSize { get; set; }
        public int MarketConfirmationWindowSize { get; set; }
    }
}
