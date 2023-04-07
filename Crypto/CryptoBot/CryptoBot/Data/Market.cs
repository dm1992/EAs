﻿using CryptoBot.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CryptoBot.Data.Miha
{
    public class Market : IMarket
    {
        public string Id { get; private set; }
        public string Symbol { get; set; }
        public DateTime CreatedAt { get; set; }
        public decimal SymbolLatestPrice { get; set; }
        public List<PriceClosure> SymbolPriceClosures { get; set; }
        public decimal? AveragePriceClosureDelta 
        { 
            get
            {
                if (this.SymbolPriceClosures.IsNullOrEmpty())
                    return null;

                return this.SymbolPriceClosures.Average(x => x.DeltaPrice);
            }
        }
        public decimal SymbolAverageVolume { get; set; }

        public Market(string symbol, List<PriceClosure> symbolPriceClosures, decimal symbolLatestPrice, decimal symbolAverageVolume)
        {
            this.Symbol = symbol;
            this.Id = Guid.NewGuid().ToString();
            this.CreatedAt = DateTime.Now;
            this.SymbolPriceClosures = symbolPriceClosures;
            this.SymbolLatestPrice = symbolLatestPrice;
            this.SymbolAverageVolume = symbolAverageVolume;
        }

        public MarketDirection GetMarketDirection()
        {
            if (this.SymbolPriceClosures.IsNullOrEmpty())
                return MarketDirection.Unknown;

            decimal? price = null;
            decimal? deltaPrice = null;
            int priceMoveUps = 0;
            int priceMoveDowns = 0;

            foreach (var symbolPriceClosure in this.SymbolPriceClosures)
            {
                try
                {
                    if (!price.HasValue && !deltaPrice.HasValue)
                    {
                        continue;
                    }
                    else if (price.Value > symbolPriceClosure.LatestPrice && deltaPrice.Value > this.AveragePriceClosureDelta)
                    {
                        priceMoveUps++;
                    }
                    else if (price.Value < symbolPriceClosure.LatestPrice && deltaPrice.Value < this.AveragePriceClosureDelta)
                    {
                        priceMoveDowns++;
                    }
                }
                finally
                {
                    price = symbolPriceClosure.LatestPrice;
                    deltaPrice = symbolPriceClosure.DeltaPrice;
                }
            }

            MarketDirection marketDirection = MarketDirection.Unknown;
            decimal percentageMoveUps = (priceMoveUps / this.SymbolPriceClosures.Count()) * 100.0m;
            decimal percentageMoveDowns = (priceMoveDowns / this.SymbolPriceClosures.Count()) * 100.0m;
            decimal percentageMoveNeutrals = 100 - percentageMoveUps - percentageMoveDowns;

            if (percentageMoveUps > percentageMoveNeutrals && percentageMoveDowns > percentageMoveNeutrals)
            {
                if (percentageMoveUps > percentageMoveDowns)
                {
                    marketDirection = this.SymbolLatestPrice > price ? MarketDirection.StrongUp : MarketDirection.WeakUp;
                }
                else if (percentageMoveDowns > percentageMoveUps)
                {
                    marketDirection = this.SymbolLatestPrice < price ? MarketDirection.StrongDown : MarketDirection.WeakDown;
                }
                else
                {
                    marketDirection = MarketDirection.Neutral;
                }
            }

            return marketDirection;
        }

        public MarketVolumeIntensity GetMarketVolumeIntensity()
        {
            if (this.SymbolPriceClosures.IsNullOrEmpty())
                return MarketVolumeIntensity.Unknown;

            decimal volume = this.SymbolPriceClosures.Sum(x => x.BuyerQuantity + x.SellerQuantity);

            if (volume > this.SymbolAverageVolume)
            {
                return MarketVolumeIntensity.Big;
            }
            else if (volume < this.SymbolAverageVolume)
            {
                return MarketVolumeIntensity.Low;
            }

            return MarketVolumeIntensity.Average;
        }

        public string Dump()
        {
            return $"{this.Symbol} market - market direction: {this.GetMarketDirection()}, market volume intensity: {this.GetMarketVolumeIntensity()}.";
        }
    }
}
