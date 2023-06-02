using Bybit.Net.Enums;
using Bybit.Net.Objects.Models.V5;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CryptoBot.Models
{
    /// <summary>
    /// Base market entity which is consisted with real and limit market data.
    /// </summary>
    public class MarketEntity
    {
        public string Symbol { get; set; }
        public DateTime CreatedAt { get; set; }

        /// <summary>
        /// Real market trades.
        /// </summary>
        public IEnumerable<BybitTrade> Trades { get; set; }

        /// <summary>
        /// Limit market trades (asks/bids).
        /// </summary>
        public BybitOrderbook Orderbook { get; set; }

        public MarketEntity(string symbol)
        {
            this.Symbol = symbol;
            this.CreatedAt = DateTime.Now;
        }
    }
}
