using CryptoBot.Models;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CryptoBot
{
    public static class Extensions
    {
        public static bool IsNullOrEmpty<T>(this IEnumerable<T> list)
        {
            if (list == null || !list.Any())
                return true;

            return false;
        }

        public static decimal? Average(this IEnumerable<decimal> list)
        {
            if (list.IsNullOrEmpty())
                return null;

            return list.Average(x => x);
        }

        public static IEnumerable<T> ParseCsv<T>(this string csv)
        {
            if (String.IsNullOrWhiteSpace(csv)) yield break;

            foreach (var d in csv.Split(','))
            {
                yield return (T)Convert.ChangeType(d.Trim(), typeof(T));
            }
        }

        public static object ParseObject(string objectRawData, string key)
        {
            try
            {
                if (String.IsNullOrEmpty(objectRawData) || String.IsNullOrEmpty(key))
                    return null;

                JObject @object = JObject.Parse(objectRawData);
                switch (key)
                {
                    case "topic":
                        string data = Convert.ToString(@object[key]);
                        return data.Replace("trade.", String.Empty);

                    default:
                        return null;
                }

            }
            catch (Exception)
            {
                return null;
            }
        }

        public static string DictionaryToString<TKey, TValue>(this IDictionary<TKey, TValue> dictionary)
        {
            return "{" + string.Join(",", dictionary.Select(kv => kv.Key + "=" + kv.Value).ToArray()) + "}";
        }

        public static VolumeDirection GetMarketEntityWindowVolumeDirection(this IEnumerable<MarketEntity> marketEntityWindow, MarketEntityWindowVolumeSetting volumeSetting)
        {
            if (volumeSetting == null || marketEntityWindow.IsNullOrEmpty())
            {
                return VolumeDirection.Unknown;
            }

            int buyVolumes = 0;
            int sellVolumes = 0;
            int unknownVolumes = 0;

            foreach (var marketEntity in marketEntityWindow)
            {
                VolumeDirection volumeDirection = marketEntity.GetVolumeDirection(volumeSetting.OrderbookDepth);

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

            if (buyVolumesPercentage > volumeSetting.BuyVolumesPercentageLimit)
            {
                return VolumeDirection.Buy;
            }
            else if (sellVolumesPercentage > volumeSetting.SellVolumesPercentageLimit)
            {
                return VolumeDirection.Sell;
            }

            return VolumeDirection.Unknown;
        }

        public static PriceChangeDirection GetMarketEntityWindowPriceChangeDirection(this IEnumerable<MarketEntity> marketEntityWindow, MarketEntityWindowPriceChangeSetting priceChangeSetting)
        {
            if (priceChangeSetting == null || marketEntityWindow.IsNullOrEmpty())
            {
                return PriceChangeDirection.Unknown;
            }

            var orderedMarketEntityWindow = marketEntityWindow.OrderBy(x => x.CreatedAt);
            decimal firstMarketEntityPrice = orderedMarketEntityWindow.First().Price;
            decimal lastMarketEntityPrice = orderedMarketEntityWindow.Last().Price;

            decimal priceChangePercentage = ((lastMarketEntityPrice - firstMarketEntityPrice) / Math.Abs(firstMarketEntityPrice)) * 100.0m;

            if (priceChangePercentage > priceChangeSetting.UpPriceChangePercentageLimit)
            {
                return PriceChangeDirection.Up;
            }
            else if (priceChangePercentage < priceChangeSetting.DownPriceChangePercentageLimit)
            {
                return PriceChangeDirection.Down;
            }

            return PriceChangeDirection.Unknown;
        }
    }
}
