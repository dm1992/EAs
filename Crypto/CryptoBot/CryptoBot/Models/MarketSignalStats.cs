using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CryptoBot.Models
{
    public class MarketSignalStats
    {
        public decimal TotalROI { get; set; }
        public decimal TotalProfitROI { get; set; }
        public decimal TotalLossROI { get; set; }
        public decimal AverageTotalROI
        {
            get
            {
                if (this.TotalMarketSignals == 0)
                    return 0;

                return this.TotalROI / this.TotalMarketSignals;
            }
        }
        public decimal AverageProfitROI 
        { 
            get 
            {
                if (this.ProfitMarketSignals == 0)
                    return 0;

                return this.TotalProfitROI / this.ProfitMarketSignals; 
            } 
        }
        public decimal AverageLossROI 
        { 
            get 
            {
                if (this.LossMarketSignals == 0)
                    return 0;

                return this.TotalLossROI / this.LossMarketSignals;
            } 
        }
        public int TotalMarketSignals { get; set; }
        public int ProfitMarketSignals { get; set; }
        public int LossMarketSignals { get; set; }
        public int NeutralMarketSignals { get; set; }

        public string Dump()
        {
            return $"\n---------------------------------------\n" +
                   $" MARKET SIGNAL STATISTICS \n" +
                   $"---------------------------------------\n" +
                   $"TotalROI: {this.TotalROI}$ (TotalProfitROI: {this.TotalProfitROI}$, TotalLossROI: {this.TotalLossROI}$)\n" +
                   $"AverageTotalROI: {this.AverageTotalROI}$\n" +
                   $"AverageProfitROI: {this.AverageProfitROI}$\n" +
                   $"AverageLossROI: {this.AverageLossROI}$\n" +
                   $"TotalMarketSignals: {this.TotalMarketSignals} (ProfitMarketSignals: {this.ProfitMarketSignals}, LossMarketSignals: {this.LossMarketSignals}, NeutralMarketSignals: {this.NeutralMarketSignals})\n" +
                   $"---------------------------------------\n";
        }
    }
}
