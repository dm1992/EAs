using Bybit.Net.Objects.Models;
using Bybit.Net.Objects.Models.Derivatives;
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
        public static MarketDirection GetOrderbookMarketDirection(List<decimal> passiveBuyVolumes, List<decimal> passiveSellVolumes)
        {
            if (passiveBuyVolumes.IsNullOrEmpty() || passiveSellVolumes.IsNullOrEmpty())
                return MarketDirection.Unknown;

            int orderbookDepth = passiveBuyVolumes.Count();

            if (passiveBuyVolumes.Count() < passiveSellVolumes.Count())
            {
                orderbookDepth = passiveBuyVolumes.Count();
            }
            else if (passiveBuyVolumes.Count() > passiveSellVolumes.Count())
            {
                orderbookDepth = passiveSellVolumes.Count();
            }

            int buys = 0;
            int sells = 0;
            int unknowns = 0;

            for (int i = 0; i < orderbookDepth; i++)
            {
                decimal passiveBuyVolume = passiveBuyVolumes.ElementAt(i);
                decimal passiveSellVolume = passiveSellVolumes.ElementAt(i);

                if (passiveBuyVolume > passiveSellVolume)
                {
                    buys++;
                }
                else if (passiveBuyVolume < passiveSellVolume)
                {
                    sells++;
                }
                else
                {
                    unknowns++;
                }
            }

            decimal buysPercentage = (buys / (decimal)(buys + sells + unknowns)) * 100.0m;
            decimal sellsPercentage = (sells / (decimal)(buys + sells + unknowns)) * 100.0m;

            if (buysPercentage > 50)
            {
                return MarketDirection.Uptrend;
            }
            else if (sellsPercentage > 50)
            {
                return MarketDirection.Downtrend;
            }

            return MarketDirection.Unknown;
        }
    }
}
