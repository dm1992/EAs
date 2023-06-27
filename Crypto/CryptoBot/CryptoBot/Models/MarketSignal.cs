using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CryptoBot.Models
{
    public class MarketSignal
    {
        public string Symbol { get; set; }

        public MarketSignal(string symbol)
        {
            this.Symbol = symbol;
        }
    }
}
