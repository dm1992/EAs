using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TradingApp.Data
{
    public class Config
    {
        public bool TradingMode { get; private set; }
        public string ApiKey { get; private set; }
        public string ApiSecret { get; private set; }
        public IEnumerable<string> Symbols { get; set; }
        public int CandleTimeframe { get; set; }
        public string MarketRawDataDirectory { get; set; }
        public string MarketAnalyzerDataDirectory { get; set; }
        public string ApplicationEventDataDirectory { get; set; }
        public int OpenOrdersPerSymbol { get; set; }

        public Config()
        {
            ParseConfig();
        }

        public void ParseConfig()
        {
            this.TradingMode = Convert.ToBoolean(ConfigurationManager.AppSettings["tradingMode"]);
            this.ApiKey = ConfigurationManager.AppSettings["apiKey"];
            this.ApiSecret = ConfigurationManager.AppSettings["apiSecret"];
            this.Symbols = ConfigurationManager.AppSettings["symbols"].ParseCsv();
            this.CandleTimeframe = Convert.ToInt32(ConfigurationManager.AppSettings["candleTimeframe"]);
            this.MarketRawDataDirectory = ConfigurationManager.AppSettings["marketRawDataDirectory"];
            this.MarketAnalyzerDataDirectory = ConfigurationManager.AppSettings["marketAnalyzerDataDirectory"];
            this.ApplicationEventDataDirectory = ConfigurationManager.AppSettings["applicationEventDataDirectory"];
            this.OpenOrdersPerSymbol = Convert.ToInt32(ConfigurationManager.AppSettings["openOrdersPerSymbol"]);
        }

        public override string ToString()
        {
            return $"Configuration ----->\n" +
                $"Trading mode: {this.TradingMode}\n" +
                $"Symbols: {String.Join(", ", Symbols)}\n" +
                $"Candle timeframe (in minutes): {this.CandleTimeframe}\n" +
                $"Market raw data directory: {this.MarketRawDataDirectory}\n" +
                $"Market analyzer data directory: {this.MarketAnalyzerDataDirectory}\n" +
                $"Application event data directory: {this.ApplicationEventDataDirectory}\n" +
                $"Open orders per symbol: {this.OpenOrdersPerSymbol}\n\n";
        }
    }
}
