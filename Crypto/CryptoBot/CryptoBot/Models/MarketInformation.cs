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
        public MarketVolumeComponent MarketVolumeComponent { get; set; }
        public MarketPriceComponent MarketPriceComponent { get; set; }

        public MarketDirection PreferedMarketDirection { get { return GetPreferedMarketDirection(); } }
        private MarketDirection GetPreferedMarketDirection()
        {
            if (this.MarketVolumeComponent == null || this.MarketPriceComponent == null)
                return MarketDirection.Unknown;

            Dictionary<int, VolumeDirection> marketEntitySubwindowVolumeDirections = this.MarketVolumeComponent.GetMarketEntitySubwindowVolumeDirections();

            if (marketEntitySubwindowVolumeDirections.IsNullOrEmpty())
                return MarketDirection.Unknown;

            Dictionary<int, PriceChangeDirection> marketEntitySubwindowPriceChangeDirections = this.MarketPriceComponent.GetMarketEntitySubwindowPriceChangeDirections();

            if (marketEntitySubwindowPriceChangeDirections.IsNullOrEmpty())
                return MarketDirection.Unknown;


            VolumeDirection marketEntityWindowVolumeDirection = this.MarketVolumeComponent.GetMarketEntityWindowVolumeDirection();
            decimal activeBuyVolume = this.MarketVolumeComponent.GetActiveBuyVolume();
            decimal activeSellVolume = this.MarketVolumeComponent.GetActiveSellVolume();
            decimal passiveBuyVolume = this.MarketVolumeComponent.GetPassiveBuyVolume();
            decimal passiveSellVolume = this.MarketVolumeComponent.GetPassiveSellVolume();


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

        public MarketInformation(string symbol)
        {
            this.Symbol = symbol;
            this.Id = Guid.NewGuid().ToString();
            this.CreatedAt = DateTime.Now;
        }

        public override string ToString()
        {
            return $"\n{this.Symbol} MARKET INFORMATION. D: {this.PreferedMarketDirection}.\n{this.MarketVolumeComponent}\n{this.MarketPriceComponent}\n";
        }
    }

    public class MarketVolumeComponent
    {
        public string Symbol { get; set; }

        /// <summary>
        /// Source of market volume component data.
        /// </summary>
        public List<MarketEntity> MarketEntityWindow { get; set; }

        public MarketEntityWindowVolumeSetting VolumeSetting { get; set; }

        public MarketVolumeComponent(string symbol, MarketEntityWindowVolumeSetting volumeSetting)
        {
            this.Symbol = symbol;
            this.VolumeSetting = volumeSetting;
        }

        public VolumeDirection GetMarketEntityWindowVolumeDirection()
        {
            return this.MarketEntityWindow.GetMarketEntityWindowVolumeDirection(this.VolumeSetting);
        }

        public Dictionary<int, VolumeDirection> GetMarketEntitySubwindowVolumeDirections()
        {
            if (this.VolumeSetting == null || this.MarketEntityWindow.IsNullOrEmpty())
            {
                return null;
            }

            Dictionary<int, VolumeDirection> volumeDirections = new Dictionary<int, VolumeDirection>();

            int marketEntitiesPerSubwindow = this.MarketEntityWindow.Count() / this.VolumeSetting.Subwindows ?? 1;
            int subwindowIndex = 0;

            for (int i = 0; i < this.MarketEntityWindow.Count(); i+= marketEntitiesPerSubwindow)
            {
                VolumeDirection volumeDirection = this.MarketEntityWindow.Skip(i).Take(marketEntitiesPerSubwindow).GetMarketEntityWindowVolumeDirection(this.VolumeSetting);

                volumeDirections.Add(subwindowIndex++, volumeDirection);
            }

            return volumeDirections;
        }

        public decimal GetActiveBuyVolume()
        {
            if (this.MarketEntityWindow.IsNullOrEmpty())
                return 0;

            return this.MarketEntityWindow.Sum(x => x.GetActiveBuyVolume());
        }

        public decimal GetActiveSellVolume()
        {
            if (this.MarketEntityWindow.IsNullOrEmpty())
                return 0;

            return this.MarketEntityWindow.Sum(x => x.GetActiveSellVolume());
        }

        public decimal GetPassiveBuyVolume(int? orderbookDepth = null)
        {
            if (this.MarketEntityWindow.IsNullOrEmpty())
                return 0;

            return this.MarketEntityWindow.Sum(x => x.GetPassiveBuyVolume(orderbookDepth));
        }

        public decimal GetPassiveSellVolume(int? orderbookDepth = null)
        {
            if (this.MarketEntityWindow.IsNullOrEmpty())
                return 0;

            return this.MarketEntityWindow.Sum(x => x.GetPassiveSellVolume(orderbookDepth));
        }

        public override string ToString()
        {
            return $"AB: {this.GetActiveBuyVolume()}, AS: {this.GetActiveSellVolume()}, PB: {this.GetPassiveBuyVolume()}, PS: {this.GetPassiveSellVolume()}. " +
                   $"MWVD: {this.GetMarketEntityWindowVolumeDirection()}. " +
                   $"SWVD: {this.GetMarketEntitySubwindowVolumeDirections().DictionaryToString()}.";
        }
    }

    public class MarketEntityWindowVolumeSetting
    {
        public int? Subwindows { get; set; }
        public int? OrderbookDepth { get; set; }
        public decimal BuyVolumesPercentageLimit { get; set; }
        public decimal SellVolumesPercentageLimit { get; set; }

    }

    public class MarketPriceComponent
    {
        public string Symbol { get; set; }

        /// <summary>
        /// Source of market price component data.
        /// </summary>
        public List<MarketEntity> MarketEntityWindow { get; set; }

        public MarketEntityWindowPriceChangeSetting PriceChangeSetting { get; set; }

        public MarketPriceComponent(string symbol, MarketEntityWindowPriceChangeSetting priceChangeSetting)
        {
            this.Symbol = symbol;
            this.PriceChangeSetting = priceChangeSetting;
        }

        public PriceChangeDirection GetMarketEntityWindowPriceChangeDirection()
        {
            return this.MarketEntityWindow.GetMarketEntityWindowPriceChangeDirection(this.PriceChangeSetting);
        }

        public Dictionary<int, PriceChangeDirection> GetMarketEntitySubwindowPriceChangeDirections()
        {
            if (this.PriceChangeSetting == null || this.MarketEntityWindow.IsNullOrEmpty())
            {
                return null;
            }

            Dictionary<int, PriceChangeDirection> priceChangeDirections = new Dictionary<int, PriceChangeDirection>();
            int marketEntitiesPerSubwindow = this.MarketEntityWindow.Count() / this.PriceChangeSetting.Subwindows ?? 1;
            int subwindowIndex = 0;

            for (int i = 0; i < this.MarketEntityWindow.Count(); i += marketEntitiesPerSubwindow)
            {
                PriceChangeDirection priceChangeDirection = this.MarketEntityWindow.Skip(i).Take(marketEntitiesPerSubwindow).GetMarketEntityWindowPriceChangeDirection(this.PriceChangeSetting);

                priceChangeDirections.Add(subwindowIndex++, priceChangeDirection);
            }

            return priceChangeDirections;
        }

        public override string ToString()
        {
            return $"WPCD: {this.GetMarketEntityWindowPriceChangeDirection()}. " +
                   $"SWPCD: {this.GetMarketEntitySubwindowPriceChangeDirections().DictionaryToString()}.";
        }
    }

    public class MarketEntityWindowPriceChangeSetting
    {
        public int? Subwindows { get; set; }
        public decimal UpPriceChangePercentageLimit { get; set; }
        public decimal DownPriceChangePercentageLimit { get; set; }

    }
}
