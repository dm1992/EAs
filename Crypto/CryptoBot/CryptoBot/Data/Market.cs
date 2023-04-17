using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CryptoBot.Data
{
    public class Market
    {
        public string Id { get; set; }
        public string Symbol { get; set; }
        public DateTime CreatedAt { get; private set; }
        public int TotalPriceLevels { get; set; }
        public int StrongBuyerVolumePriceLevels { get; set; }
        public int StrongSellerVolumePriceLevels { get; set; }
        public MarketDirection MarketDirection { get; set; }
        public decimal StrongBuyerVolumePriceLevelsPercentage
        {
            get
            {
                if (this.TotalPriceLevels == 0)
                    return 0;

                return Math.Round(this.StrongBuyerVolumePriceLevels / this.TotalPriceLevels * 100.0M, 3);
            }
        }
        public decimal StrongSellerVolumePriceLevelsPercentage
        {
            get
            {
                if (this.TotalPriceLevels == 0)
                    return 0;

                return Math.Round(this.StrongSellerVolumePriceLevels / this.TotalPriceLevels * 100.0M, 3);
            }
        }

        public Market(string symbol)
        {
            this.Id = Guid.NewGuid().ToString();
            this.Symbol = symbol;
            this.CreatedAt = DateTime.UtcNow;
        }

        public string Dump()
        {
            return $"From total {this.TotalPriceLevels} price levels there are {this.StrongBuyerVolumePriceLevels} strong buyer volume ({this.StrongBuyerVolumePriceLevelsPercentage} %) " +
                   $"and {this.StrongSellerVolumePriceLevels} strong seller volume ({this.StrongSellerVolumePriceLevelsPercentage} %) price levels.\n" +
                   $"Current market direction is {this.MarketDirection}.";
        }
    }
}
