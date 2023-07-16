using Bybit.Net.Objects.Models;
using Bybit.Net.Objects.Models.V5;
using CryptoBot.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace CryptoBot
{
    public static class Helpers
    {
        public static string OrderbookToString(IEnumerable<BybitOrderBookEntry> orderbook, int depth = 10)
        {
            if (orderbook.IsNullOrEmpty())
                return String.Empty;

            return "{" + string.Join(", ", orderbook.Take(depth).Select(x => x.Quantity).ToArray()) + "}";
        }

        public static VolumeDirection GetMarketEntityWindowVolumeDirection(IEnumerable<MarketEntity> marketEntityWindow, decimal buyVolumesPercentageLimit = 60, decimal sellVolumesPercentageLimit = 60)
        {
            if (marketEntityWindow.IsNullOrEmpty())
            {
                return VolumeDirection.Unknown;
            }

            int buyVolumes = 0;
            int sellVolumes = 0;
            int unknownVolumes = 0;

            foreach (var marketEntity in marketEntityWindow)
            {
                VolumeDirection volumeDirection = marketEntity.GetVolumeDirection();

                switch (volumeDirection)
                {
                    case VolumeDirection.Buy:
                        buyVolumes++;
                        break;

                    case VolumeDirection.Sell:
                        sellVolumes++;
                        break;

                    default:
                        unknownVolumes++;
                        break;
                }
            }

            decimal buyVolumesPercentage = (buyVolumes / (decimal)(buyVolumes + sellVolumes + unknownVolumes)) * 100.0m;
            decimal sellVolumesPercentage = (sellVolumes / (decimal)(buyVolumes + sellVolumes + unknownVolumes)) * 100.0m;

            if (buyVolumesPercentage > buyVolumesPercentageLimit)
            {
                return VolumeDirection.Buy;
            }
            else if (sellVolumesPercentage > sellVolumesPercentageLimit)
            {
                return VolumeDirection.Sell;
            }

            return VolumeDirection.Unknown;
        }

        public static PriceDirection GetMarketEntityWindowPriceDirection(IEnumerable<MarketEntity> marketEntityWindow, decimal priceUpPercentageLimit = 0, decimal priceDownPercentageLimit = 0)
        {
            if (marketEntityWindow.IsNullOrEmpty())
            {
                return PriceDirection.Unknown;
            }

            List<MarketEntity> orderedMarketEntityWindow = marketEntityWindow.OrderBy(x => x.CreatedAt).ToList();
            decimal firstMarketEntityPrice = orderedMarketEntityWindow.First().Price;
            decimal lastMarketEntityPrice = orderedMarketEntityWindow.Last().Price;

            decimal priceChangePercentage = ((lastMarketEntityPrice - firstMarketEntityPrice) / Math.Abs(firstMarketEntityPrice)) * 100.0m;

            if (priceChangePercentage > priceUpPercentageLimit)
            {
                return PriceDirection.Up;
            }
            else if (priceChangePercentage < priceDownPercentageLimit)
            {
                return PriceDirection.Down;
            }

            return PriceDirection.Unknown;
        }
    }
}
