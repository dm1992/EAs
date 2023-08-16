using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CryptoBot.Models
{
    public class MarketEvaluationWindow : MarketEntityWindow
    {
        public bool IsActive { get; set; }

        public MarketEvaluationWindow(string symbol) : base(symbol)
        {
            this.IsActive = false;
        }

        public string Dump()
        {
            string prefix =
            $"\n---------------------------------------\n" +
            $"{this.Symbol} MARKET EVALUATION WINDOW\n" +
            $"---------------------------------------\n";

            return prefix + base.Dump();
        }
    }

    public class MarketConfirmationWindow : MarketEntityWindow
    {
        public MarketConfirmationWindow(string symbol) : base(symbol)
        {

        }

        public string Dump()
        {
            string prefix =
            $"\n---------------------------------------\n" +
            $"{this.Symbol} MARKET CONFIRMATION WINDOW\n" +
            $"---------------------------------------\n";

            return prefix + base.Dump();
        }
    }

    public abstract class MarketEntityWindow
    {
        public string Id { get; private set; }
        public string Symbol { get; set; }
        public DateTime CreatedAt { get; set; }
        public List<MarketEntity> MarketEntities { get; set; }

        public MarketEntityWindow(string symbol)
        {
            this.Id = Guid.NewGuid().ToString();
            this.Symbol = symbol;
            this.CreatedAt = DateTime.Now;
        }

        public decimal GetTotalActiveBuyVolume()
        {
            if (this.MarketEntities.IsNullOrEmpty())
                return 0;

            return this.MarketEntities.Sum(x => x.GetActiveBuyVolume());
        }

        public decimal GetTotalActiveSellVolume()
        {
            if (this.MarketEntities.IsNullOrEmpty())
                return 0;

            return this.MarketEntities.Sum(x => x.GetActiveSellVolume());
        }

        public decimal GetTotalActiveBuyVolumePercentage()
        {
            decimal totalActiveBuyVolume = GetTotalActiveBuyVolume();
            decimal totalActiveVolume = totalActiveBuyVolume + GetTotalActiveSellVolume();

            if (totalActiveVolume == 0)
                return 0;

            return (totalActiveBuyVolume / totalActiveVolume) * 100.0m;
        }

        public decimal GetTotalActiveSellVolumePercentage()
        {
            decimal totalActiveSellVolume = GetTotalActiveSellVolume();
            decimal totalActiveVolume = totalActiveSellVolume + GetTotalActiveBuyVolume();

            if (totalActiveVolume == 0)
                return 0;

            return (totalActiveSellVolume / totalActiveVolume) * 100.0m;
        }

        public decimal GetAverageActiveBuyVolume()
        {
            if (this.MarketEntities.IsNullOrEmpty())
                return 0;

            return this.MarketEntities.Average(x => x.GetActiveBuyVolume());
        }

        public decimal GetAverageActiveSellVolume()
        {
            if (this.MarketEntities.IsNullOrEmpty())
                return 0;

            return this.MarketEntities.Average(x => x.GetActiveSellVolume());
        }

        public List<decimal> GetAveragePassiveBuyVolumes(int orderbookDepth = 10)
        {
            if (this.MarketEntities.IsNullOrEmpty())
                return Enumerable.Empty<decimal>().ToList();

            List<decimal> averagePassiveBuyVolumes = new List<decimal>(orderbookDepth);

            for (int i = 0; i < orderbookDepth; i++)
            {
                averagePassiveBuyVolumes.Add(this.MarketEntities.Average(x => x.GetPassiveBuyVolume(i)));
            }

            return averagePassiveBuyVolumes;
        }

        public List<decimal> GetAveragePassiveSellVolumes(int orderbookDepth = 10)
        {
            if (this.MarketEntities.IsNullOrEmpty())
                return Enumerable.Empty<decimal>().ToList();

            List<decimal> averagePassiveSellVolumes = new List<decimal>(orderbookDepth);

            for (int i = 0; i < orderbookDepth; i++)
            {
                averagePassiveSellVolumes.Add(this.MarketEntities.Average(x => x.GetPassiveSellVolume(i)));
            }

            return averagePassiveSellVolumes;
        }

        public int FindMaximumAveragePassiveBuyVolumeOrderbookDepth()
        {
            List<decimal> averagePassiveBuyVolumes = GetAveragePassiveBuyVolumes();
            decimal maxValue = decimal.MinValue;
            int orderbookDepth = -1;

            for (int i = 0; i < averagePassiveBuyVolumes.Count; i++)
            {
                decimal value = averagePassiveBuyVolumes.ElementAt(i);

                if (value > maxValue)
                {
                    maxValue = value;
                    orderbookDepth = i + 1;
                }
            }

            return orderbookDepth;
        }

        public int FindMaximumAveragePassiveSellVolumeOrderbookDepth()
        {
            List<decimal> averagePassiveSellVolumes = GetAveragePassiveSellVolumes();
            decimal maxValue = decimal.MinValue;
            int orderbookDepth = -1;

            for (int i = 0; i < averagePassiveSellVolumes.Count; i++)
            {
                decimal value = averagePassiveSellVolumes.ElementAt(i);

                if (value > maxValue)
                {
                    maxValue = value;
                    orderbookDepth = i + 1;
                }
            }

            return orderbookDepth;
        }

        public decimal GetAveragePrice()
        {
            if (this.MarketEntities.IsNullOrEmpty())
                return 0;

            return this.MarketEntities.Average(x => x.Price);
        }

        public decimal GetPriceChangePercentage()
        {
            List<MarketEntity> orderedMarketEntities = this.MarketEntities.OrderBy(x => x.CreatedAt).ToList();
            decimal firstMarketEntityPrice = orderedMarketEntities.First().Price;
            decimal lastMarketEntityPrice = orderedMarketEntities.Last().Price;

            return ((lastMarketEntityPrice - firstMarketEntityPrice) / Math.Abs(firstMarketEntityPrice)) * 100.0m;
        }

        public virtual MarketDirection GetMarketDirection()
        {
            MarketDirection marketDirection = MarketDirection.Unknown;

            if (this.GetTotalActiveBuyVolumePercentage() > 50)
            {
                int maximumAveragePassiveBuyVolumeOrderbookDepth = FindMaximumAveragePassiveBuyVolumeOrderbookDepth();
                int maximumAveragePassiveSellVolumeOrderbookDepth = FindMaximumAveragePassiveSellVolumeOrderbookDepth();

                if (maximumAveragePassiveBuyVolumeOrderbookDepth < maximumAveragePassiveSellVolumeOrderbookDepth)
                {
                    // check what is happening with passive buy and sell volume down to strongest average passive buy volume
                    List<decimal> passiveBuyVolumes = this.GetAveragePassiveBuyVolumes(maximumAveragePassiveBuyVolumeOrderbookDepth);
                    List<decimal> passiveSellVolumes = this.GetAveragePassiveSellVolumes(maximumAveragePassiveBuyVolumeOrderbookDepth);

                    if (Helpers.GetOrderbookMarketDirection(passiveBuyVolumes, passiveSellVolumes) == MarketDirection.Uptrend)
                    {
                        if (this.GetPriceChangePercentage() > 0)
                        {
                            marketDirection = MarketDirection.Uptrend;
                        }
                    }
                }
            }
            else if (this.GetTotalActiveSellVolumePercentage() > 50)
            {
                int maximumAveragePassiveBuyVolumeOrderbookDepth = FindMaximumAveragePassiveBuyVolumeOrderbookDepth();
                int maximumAveragePassiveSellVolumeOrderbookDepth = FindMaximumAveragePassiveSellVolumeOrderbookDepth();

                if (maximumAveragePassiveSellVolumeOrderbookDepth < maximumAveragePassiveBuyVolumeOrderbookDepth)
                {
                    // check what is happening with passive buy and sell volume down to strongest average passive sell volume
                    List<decimal> passiveBuyVolumes = this.GetAveragePassiveBuyVolumes(maximumAveragePassiveSellVolumeOrderbookDepth);
                    List<decimal> passiveSellVolumes = this.GetAveragePassiveSellVolumes(maximumAveragePassiveSellVolumeOrderbookDepth);

                    if (Helpers.GetOrderbookMarketDirection(passiveBuyVolumes, passiveSellVolumes) == MarketDirection.Downtrend)
                    {
                        if (this.GetPriceChangePercentage() < 0)
                        {
                            marketDirection = MarketDirection.Downtrend;
                        }
                    }
                }
            }

            return marketDirection;
        }

        public string Dump(int orderbookDepth = 10)
        {
            return $"BUY VOLUME\n" +
                  $"---------------------------------------\n" +
                  $"TotalActiveBuyVolume: {this.GetTotalActiveBuyVolume()}\n" +
                  $"TotalActiveBuyVolumePercentage: {Math.Round(this.GetTotalActiveBuyVolumePercentage(), 3)}%\n" +
                  $"AverageActiveBuyVolume: {this.GetAverageActiveBuyVolume()}\n" +
                  $"AveragePassiveBuyVolumes: {this.GetAveragePassiveBuyVolumes(orderbookDepth).ListToString()}\n" +
                  $"---------------------------------------\n" +
                  $"SELL VOLUME\n" +
                  $"---------------------------------------\n" +
                  $"TotalActiveSellVolume: {this.GetTotalActiveSellVolume()}\n" +
                  $"TotalActiveSellVolumePercentage: {Math.Round(this.GetTotalActiveSellVolumePercentage(), 3)}%\n" +
                  $"AverageActiveSellVolume: {this.GetAverageActiveSellVolume()}\n" +
                  $"AveragePassiveSellVolumes: {this.GetAveragePassiveSellVolumes(orderbookDepth).ListToString()}\n" +
                  $"---------------------------------------\n" +
                  $"PRICE\n" +
                  $"---------------------------------------\n" +
                  $"AveragePrice: {this.GetAveragePrice()}$\n" +
                  $"PriceChangePercentage: {this.GetPriceChangePercentage()}%\n" +
                  $"---------------------------------------\n" +
                  $">>> MARKET DIRECTION: {this.GetMarketDirection()} >>>\n" +
                  $"---------------------------------------\n";
        }
    }
}
