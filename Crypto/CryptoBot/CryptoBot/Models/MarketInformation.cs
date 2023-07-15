using Bybit.Net.Objects.Models.V5;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CryptoBot.Models
{
    public class MarketInformation
    {
        public string Id { get; private set; }
        public string Symbol { get; set; }
        public DateTime CreatedAt { get; private set; }
        public Volume Volume { get; set; }
        public Price Price { get; set; }

        public MarketInformation(string symbol)
        {
            this.Symbol = symbol;
            this.Id = Guid.NewGuid().ToString();
            this.CreatedAt = DateTime.Now;
        }

        public MarketDirection GetMarketDirection()
        {
            if (this.Volume == null || this.Price == null)
            {
                return MarketDirection.Unknown;
            }

            Dictionary<int, VolumeDirection> marketEntitySubwindowVolumeDirections = this.Volume.GetMarketEntitySubwindowVolumeDirections();

            if (marketEntitySubwindowVolumeDirections.IsNullOrEmpty())
            {
                return MarketDirection.Unknown;
            }

            Dictionary<int, PriceDirection> marketEntitySubwindowPriceDirections = this.Price.GetMarketEntitySubwindowPriceDirections();

            if (marketEntitySubwindowPriceDirections.IsNullOrEmpty())
            {
                return MarketDirection.Unknown;
            }

            VolumeDirection marketEntityWindowVolumeDirection = this.Volume.GetMarketEntityWindowVolumeDirection();
            decimal activeBuyVolume = this.Volume.GetMarketEntityWindowActiveBuyVolume();
            decimal activeSellVolume = this.Volume.GetMarketEntityWindowActiveSellVolume();
            decimal passiveBuyVolume = this.Volume.GetMarketEntityWindowPassiveBuyVolume();
            decimal passiveSellVolume = this.Volume.GetMarketEntityWindowPassiveSellVolume();

            if (marketEntityWindowVolumeDirection == VolumeDirection.Buy)
            {
                // is buy volume concentrated on the beginging of market entity window
                // has price reacted and if it is check total volume proportions (later)

                if (marketEntitySubwindowVolumeDirections[0] == VolumeDirection.Buy) // for now only first subwindow
                {
                    //if (marketEntitySubwindowPriceChangeDirections[0] == PriceChangeDirection.Up)
                    //{
                        if (activeBuyVolume > activeSellVolume)
                        {
                            if (activeBuyVolume + passiveBuyVolume > passiveSellVolume)
                            {
                                return MarketDirection.Uptrend;
                            }
                        }
                    //}
                }

            }
            else if (marketEntityWindowVolumeDirection == VolumeDirection.Sell)
            {
                if (marketEntitySubwindowVolumeDirections[0] == VolumeDirection.Sell)
                {
                    //if (marketEntitySubwindowPriceChangeDirections[0] == PriceChangeDirection.Down)
                    //{
                        if (activeSellVolume > activeBuyVolume)
                        {
                            if (activeSellVolume + passiveSellVolume > passiveBuyVolume)
                            {
                                return MarketDirection.Downtrend;
                            }
                        }
                   // }
                }
            }

            return MarketDirection.Unknown;
        }

        public string Dump()
        {
            return $"\n---------------------------------------\n" +
                   $"{this.Symbol} MARKET INFORMATION\n" +
                   $"---------------------------------------\n" +
                   $"D: {this.GetMarketDirection()}\n" +
                   $"{this.Volume.Dump()}\n" +
                   $"{this.Price.Dump()}\n" +
                   $"---------------------------------------\n";
        }
    }

    public class Volume
    {
        public string Symbol { get; set; }
        public BybitOrderbook Orderbook { get; set; }
        public List<MarketEntity> MarketEntityWindow { get; set; }

        public Volume(string symbol)
        {
            this.Symbol = symbol;
        }

        public VolumeDirection GetMarketEntityWindowVolumeDirection()
        {
            return Helpers.GetMarketEntityWindowVolumeDirection(this.MarketEntityWindow);
        }

        public Dictionary<int, VolumeDirection> GetMarketEntitySubwindowVolumeDirections(int subwindows = 3)
        {
            Dictionary<int, VolumeDirection> volumeDirections = new Dictionary<int, VolumeDirection>();

            if (this.MarketEntityWindow.IsNullOrEmpty())
            {
                return volumeDirections;
            }

            int marketEntitiesPerSubwindow = this.MarketEntityWindow.Count() / subwindows;
            int subwindowIndex = 0;

            for (int i = 0; i < this.MarketEntityWindow.Count(); i+= marketEntitiesPerSubwindow)
            {
                VolumeDirection volumeDirection = Helpers.GetMarketEntityWindowVolumeDirection(this.MarketEntityWindow.Skip(i).Take(marketEntitiesPerSubwindow));
                volumeDirections.Add(subwindowIndex++, volumeDirection);
            }

            return volumeDirections;
        }

        public decimal GetMarketEntityWindowActiveBuyVolume()
        {
            if (this.MarketEntityWindow.IsNullOrEmpty())
                return 0;

            return this.MarketEntityWindow.Sum(x => x.GetActiveBuyVolume());
        }

        public decimal GetMarketEntityWindowActiveSellVolume()
        {
            if (this.MarketEntityWindow.IsNullOrEmpty())
                return 0;

            return this.MarketEntityWindow.Sum(x => x.GetActiveSellVolume());
        }

        public decimal GetMarketEntityWindowPassiveBuyVolume()
        {
            if (this.MarketEntityWindow.IsNullOrEmpty())
                return 0;

            return this.MarketEntityWindow.Sum(x => x.GetPassiveBuyVolume());
        }

        public decimal GetMarketEntityWindowPassiveSellVolume()
        {
            if (this.MarketEntityWindow.IsNullOrEmpty())
                return 0;

            return this.MarketEntityWindow.Sum(x => x.GetPassiveSellVolume());
        }

        public string Dump()
        {
            return $"\n----------VOLUME--------------\n" +
                   $"WAB: {this.GetMarketEntityWindowActiveBuyVolume()}, WAS: {this.GetMarketEntityWindowActiveSellVolume()}, WPB: {this.GetMarketEntityWindowPassiveBuyVolume()}, WPS: {this.GetMarketEntityWindowPassiveSellVolume()}\n" +
                   $"WVD: {this.GetMarketEntityWindowVolumeDirection()}, SWVD: {this.GetMarketEntitySubwindowVolumeDirections().DictionaryToString()}\n" +
                   $"--------------------------------\n" +
                   $"----------ORDERBOOK-------------\n" +
                   $"Bids: {Helpers.OrderbookToString(this.Orderbook?.Bids)}\n" +
                   $"Asks: {Helpers.OrderbookToString(this.Orderbook?.Asks)}\n" +
                   $"--------------------------------";
        }
    }

    public class Price
    {
        public string Symbol { get; set; }
        public List<MarketEntity> MarketEntityWindow { get; set; }

        public Price(string symbol)
        {
            this.Symbol = symbol;
        }

        public PriceDirection GetMarketEntityWindowPriceDirection()
        {
            return Helpers.GetMarketEntityWindowPriceDirection(this.MarketEntityWindow);
        }

        public Dictionary<int, PriceDirection> GetMarketEntitySubwindowPriceDirections(int subwindows = 3)
        {
            Dictionary<int, PriceDirection> priceDirections = new Dictionary<int, PriceDirection>();

            if (this.MarketEntityWindow.IsNullOrEmpty())
            {
                return priceDirections;
            }

            int marketEntitiesPerSubwindow = this.MarketEntityWindow.Count() / subwindows;
            int subwindowIndex = 0;

            for (int i = 0; i < this.MarketEntityWindow.Count(); i += marketEntitiesPerSubwindow)
            {
                PriceDirection priceDirection = Helpers.GetMarketEntityWindowPriceDirection(this.MarketEntityWindow.Skip(i).Take(marketEntitiesPerSubwindow));
                priceDirections.Add(subwindowIndex++, priceDirection);
            }

            return priceDirections;
        }

        public string Dump()
        {
            return  $"\n----------PRICE--------------\n" +
                    $"WPD: {this.GetMarketEntityWindowPriceDirection()}, SWPD: {this.GetMarketEntitySubwindowPriceDirections().DictionaryToString()}\n" +
                    $"--------------------------------";
        }
    }
}
