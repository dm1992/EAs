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
    /// Base market entity model.
    /// </summary>
    public class MarketEntity
    {
        public string Id { get; private set; }
        public string Symbol { get; set; }
        public decimal Price { get; set; }
        public DateTime CreatedAt { get; set; }
        public IEnumerable<BybitTrade> MarketTrades { get; set; }
        public BybitOrderbook Orderbook { get; set; }

        public MarketEntity(string symbol)
        {
            this.Symbol = symbol;
            this.Id = Guid.NewGuid().ToString();
            this.CreatedAt = DateTime.Now;
        }

        public VolumeDirection GetVolumeDirection(int? orderbookDepth = null)
        {
            decimal activeBuyVolume = GetActiveBuyVolume();
            decimal activeSellVolume = GetActiveSellVolume();
            decimal passiveBuyVolume = GetPassiveBuyVolume(orderbookDepth);
            decimal passiveSellVolume = GetPassiveSellVolume(orderbookDepth);

            if (activeBuyVolume > activeSellVolume)
            {
                if (activeBuyVolume + passiveBuyVolume > passiveSellVolume)
                {
                    return VolumeDirection.Buy;
                }
            }
            else if (activeSellVolume > activeBuyVolume)
            {
                if (activeSellVolume + passiveSellVolume > passiveBuyVolume)
                {
                    return VolumeDirection.Sell;
                }
            }

            return VolumeDirection.Unknown;
        }

        public decimal GetActiveBuyVolume()
        {
            lock (this)
            {
                if (this.MarketTrades.IsNullOrEmpty())
                    return 0;

                return this.MarketTrades.Where(x => x.Side == OrderSide.Buy).Sum(x => x.Quantity);
            }
        }

        public decimal GetActiveSellVolume()
        {
            lock (this)
            {
                if (this.MarketTrades.IsNullOrEmpty())
                    return 0;

                return this.MarketTrades.Where(x => x.Side == OrderSide.Sell).Sum(x => x.Quantity);
            }
        }

        public decimal GetPassiveBuyVolume(int? orderbookDepth = null)
        {
            lock (this)
            {
                IEnumerable<BybitOrderbookEntry> bids = GetBids(orderbookDepth);
                if (bids.IsNullOrEmpty())
                    return 0;

                return bids.Sum(x => x.Quantity);
            }
        }

        public decimal GetPassiveSellVolume(int? orderbookDepth = null)
        {
            lock (this)
            {
                IEnumerable<BybitOrderbookEntry> asks = GetAsks(orderbookDepth);
                if (asks.IsNullOrEmpty())
                    return 0;

                return asks.Sum(x => x.Quantity);
            }
        }

        private IEnumerable<BybitOrderbookEntry> GetBids(int? orderbookDepth = null)
        {
            lock (this)
            {
                if (this.Orderbook == null)
                    return null;

                if (!orderbookDepth.HasValue || orderbookDepth.Value > this.Orderbook.Bids.Count())
                    return this.Orderbook.Bids;

                return this.Orderbook.Bids.Take(orderbookDepth.Value);
            }
        }

        private IEnumerable<BybitOrderbookEntry> GetAsks(int? orderbookDepth = null)
        {
            lock (this)
            {
                if (this.Orderbook == null)
                    return null;

                if (!orderbookDepth.HasValue || orderbookDepth.Value > this.Orderbook.Asks.Count())
                    return this.Orderbook.Asks;

                return this.Orderbook.Asks.Take(orderbookDepth.Value);
            }
        }
    }
}
