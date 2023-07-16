using Bybit.Net.Objects.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CryptoBot.Models
{
    public class Orderbook
    {
        public string Symbol { get; set; }
        public List<BybitOrderBookEntry> Bids { get; set; }
        public List<BybitOrderBookEntry> Asks { get; set; }
    }
}
