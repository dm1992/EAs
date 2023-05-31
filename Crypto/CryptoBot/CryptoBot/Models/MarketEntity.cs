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
        public decimal BuyerVolumePercentageLimit { get; set; }
        public decimal SellerVolumePercentageLimit { get; set; }
        public decimal? Price { get { return GetTradePrice(); } }
        public DateTime CreatedAt { get; set; }
        public bool IsDirty { get; set; }

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
            this.IsDirty = false;
        }

        public MarketEntity(string symbol, decimal buyerVolumePercentageLimit, decimal sellerVolumePercentageLimit)
        {
            this.Symbol = symbol;
            this.BuyerVolumePercentageLimit = buyerVolumePercentageLimit;
            this.SellerVolumePercentageLimit = sellerVolumePercentageLimit;
            this.CreatedAt = DateTime.Now;
            this.IsDirty = false;
        }

        public decimal GetRealBuyerVolume()
        {
            if (this.Trades.IsNullOrEmpty())
                return 0;

            return this.Trades.Where(x => x.Side == OrderSide.Buy).Select(x => x.Quantity).DefaultIfEmpty(0).Sum();
        }

        public decimal GetLimitBuyerVolume(int? orderbookDepth = null)
        {
            if (this.Orderbook == null) 
                return 0;

            int normalizedOrderbookDepth = GetNormalizedOrderbookDepth(this.Orderbook.Bids, orderbookDepth);

            return this.Orderbook.Bids.Take(normalizedOrderbookDepth).Select(x => x.Quantity).DefaultIfEmpty(0).Sum();
        }

        public decimal GetRealSellerVolume()
        {
            if (this.Trades.IsNullOrEmpty())
                return 0;

            return this.Trades.Where(x => x.Side == OrderSide.Sell).Select(x => x.Quantity).DefaultIfEmpty(0).Sum();
        }

        public decimal GetLimitSellerVolume(int? orderbookDepth = null)
        {
            if (this.Orderbook == null) 
                return 0;

            int normalizedOrderbookDepth = GetNormalizedOrderbookDepth(this.Orderbook.Asks, orderbookDepth);

            return this.Orderbook.Asks.Take(normalizedOrderbookDepth).Select(x => x.Quantity).DefaultIfEmpty(0).Sum();
        }

        public VolumeIntensity GetVolumeIntensity(int? orderbookDepth = null)
        {
            decimal realBuyerVolume = GetRealBuyerVolume();
            decimal limitBuyerVolume = GetLimitBuyerVolume(orderbookDepth);
            decimal totalBuyerVolume = realBuyerVolume + limitBuyerVolume;

            decimal realSellerVolume = GetRealSellerVolume();
            decimal limitSellerVolume = GetLimitSellerVolume(orderbookDepth);
            decimal totalSellerVolume = realSellerVolume + limitSellerVolume;

            decimal totalVolume = totalBuyerVolume + totalSellerVolume;
            decimal buyerVolumePercentage = totalBuyerVolume / totalVolume * 100.0m;
            decimal sellerVolumePercentage = totalSellerVolume / totalVolume * 100.0m;

            if (buyerVolumePercentage == sellerVolumePercentage)
            {
                return VolumeIntensity.Neutral;
            }
            else if (buyerVolumePercentage >= this.BuyerVolumePercentageLimit)
            {
                return VolumeIntensity.Buyer;
            }
            else if (sellerVolumePercentage >= this.SellerVolumePercentageLimit)
            {
                return VolumeIntensity.Seller;
            }

            return VolumeIntensity.Unknown;
        }

        public string Dump()
        {
            return $"{this.Symbol} @ {this.Price}, VolumeIntensity: {this.GetVolumeIntensity()} (RealBuyerVolume: {this.GetRealBuyerVolume()}, LimitBuyerVolume: {this.GetLimitBuyerVolume()}, RealSellerVolume: {this.GetRealSellerVolume()}, LimitSellerVolume: {this.GetLimitSellerVolume()}).";
        }

        private int GetNormalizedOrderbookDepth(IEnumerable<BybitOrderbookEntry> orderbookEntry, int? orderbookDepth)
        {
            if (orderbookEntry.IsNullOrEmpty())
                return 0;

            int actualOrderbookDepth = orderbookEntry.Count();

            if (!orderbookDepth.HasValue)
            {
                orderbookDepth = actualOrderbookDepth;
            }
            else if (orderbookDepth.Value > actualOrderbookDepth)
            {
                orderbookDepth = actualOrderbookDepth;
            }

            return orderbookDepth.Value;
        }

        private decimal? GetTradePrice()
        {
            return this.Trades?.FirstOrDefault()?.Price;
        }
    }
}
