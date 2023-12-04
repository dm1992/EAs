using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MarketAnalyzer.Configs
{
    public class BacktestConfig
    {
        [JsonProperty("version")]
        public string Version { get; set; }

        [JsonProperty("symbolConfigs")]
        public List<SymbolConfig> SymbolConfigs { get; set; }
    }

    public class SymbolConfig
    {
        [JsonProperty("symbol")]
        public string Symbol { get; set; }

        [JsonProperty("historyTradesFilePath")]
        public string HistoryTradesFilePath { get; set; }

        [JsonProperty("resultsFilePath")]
        public string ResultsFilePath { get; set; }

        [JsonProperty("tradingFeeAmount")]
        public decimal TradingFeeAmount { get; set; }

        [JsonProperty("takeProfitAmount")]
        public decimal TakeProfitAmount { get; set; }

        [JsonProperty("stopLossAmount")]
        public decimal StopLossAmount { get; set; }

        [JsonProperty("historyTradesBatchSize")]
        public int HistoryTradesBatchSize { get; set; }

        [JsonProperty("concurrentBuyMarketSignals")]
        public int ConcurrentBuyMarketSignals { get; set; }

        [JsonProperty("concurrentSellMarketSignals")]
        public int ConcurrentSellMarketSignals { get; set; }

        [JsonProperty("tradingVolume")]
        public TradingVolume TradingVolume { get; set; }
    }

    public class TradingVolume
    {
        [JsonProperty("buyLimit")]
        public decimal BuyLimit { get; set; }

        [JsonProperty("sellLimit")]
        public decimal SellLimit { get; set; }

        [JsonProperty("buyMarket")]
        public decimal BuyMarket { get; set; }

        [JsonProperty("sellMarket")]
        public decimal SellMarket { get; set; }
    }
}
