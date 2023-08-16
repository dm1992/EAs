using Bybit.Net.Enums;
using Bybit.Net.Objects.Models.Derivatives;
using Bybit.Net.Objects.Models.Socket;
using Bybit.Net.Objects.Models.Socket.Derivatives;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CryptoBot.Models
{
    public class MarketEntity
    {
        public string Id { get; private set; }
        public string Symbol { get; set; }
        public DateTime CreatedAt { get; set; }
        public decimal Price { get; set; }
        public List<BybitDerivativesTradeUpdate> MarketTrades { get; set; }
        public BybitDerivativesOrderBookEntry Orderbook { get; set; }

        public MarketEntity(string symbol)
        {
            this.Id = Guid.NewGuid().ToString();
            this.Symbol = symbol;
            this.CreatedAt = DateTime.Now;
        }

        public decimal GetActiveBuyVolume()
        {
            if (this.MarketTrades.IsNullOrEmpty())
                return 0;

            return this.MarketTrades.Where(x => x.Side == OrderSide.Buy).Sum(x => x.Quantity);
        }

        public decimal GetPassiveBuyVolume(int orderbookLevel)
        {
            if (this.Orderbook == null || this.Orderbook.Bids.IsNullOrEmpty())
                return 0;

            if (orderbookLevel >= this.Orderbook.Bids.Count())
                return 0;

            return this.Orderbook.Bids.ElementAt(orderbookLevel).Quantity;
        }

        public decimal GetActiveSellVolume()
        {
            if (this.MarketTrades.IsNullOrEmpty())
                return 0;

            return this.MarketTrades.Where(x => x.Side == OrderSide.Sell).Sum(x => x.Quantity);
        }

        public decimal GetPassiveSellVolume(int orderbookLevel)
        {
            if (this.Orderbook == null || this.Orderbook.Asks.IsNullOrEmpty())
                return 0;

            if (orderbookLevel >= this.Orderbook.Asks.Count())
                return 0;

            return this.Orderbook.Asks.ElementAt(orderbookLevel).Quantity;
        }
    }
}
