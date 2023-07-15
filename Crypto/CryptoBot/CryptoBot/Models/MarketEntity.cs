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
        public IEnumerable<BybitTrade> ActiveTrades { get; set; }
        public BybitOrderbook Orderbook { get; set; }

        public MarketEntity(string symbol)
        {
            this.Symbol = symbol;
            this.Id = Guid.NewGuid().ToString();
            this.CreatedAt = DateTime.Now;
        }

        public VolumeDirection GetVolumeDirection()
        {
            decimal activeBuyVolume = GetActiveBuyVolume();
            decimal activeSellVolume = GetActiveSellVolume();
            decimal passiveBuyVolume = GetPassiveBuyVolume();
            decimal passiveSellVolume = GetPassiveSellVolume();

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
                if (this.ActiveTrades.IsNullOrEmpty())
                    return 0;

                return this.ActiveTrades.Where(x => x.Side == OrderSide.Buy).Sum(x => x.Quantity);
            }
        }

        public decimal GetActiveSellVolume()
        {
            lock (this)
            {
                if (this.ActiveTrades.IsNullOrEmpty())
                    return 0;

                return this.ActiveTrades.Where(x => x.Side == OrderSide.Sell).Sum(x => x.Quantity);
            }
        }

        public decimal GetPassiveBuyVolume(int orderbookDepth = 50)
        {
            lock (this)
            {
                if (this.Orderbook == null)
                    return 0;

                if (orderbookDepth > this.Orderbook.Bids.Count())
                    return this.Orderbook.Bids.Sum(x => x.Quantity);

                return this.Orderbook.Bids.Take(orderbookDepth).Sum(x => x.Quantity);
            }
        }

        public decimal GetPassiveSellVolume(int orderbookDepth = 50)
        {
            lock (this)
            {
                if (this.Orderbook == null)
                    return 0;

                if (orderbookDepth > this.Orderbook.Asks.Count())
                    return this.Orderbook.Asks.Sum(x => x.Quantity);

                return this.Orderbook.Asks.Take(orderbookDepth).Sum(x => x.Quantity);
            }
        }
    }
}
