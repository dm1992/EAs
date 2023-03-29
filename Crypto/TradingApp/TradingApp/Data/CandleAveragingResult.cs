using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TradingApp.Data
{
    public class CandleAveragingResult
    {
        public decimal AverageBuyers { get; set; }
        public decimal AverageSellers { get; set; }
        public decimal AveragePositiveDelta { get; set; }
        public decimal AverageNegativeDelta { get; set; }
        public decimal AverageStrengthBuyers { get; set; }
        public decimal AverageStrengthSellers { get; set; }
        public decimal AveragePositiveStrengthDelta { get; set; }
        public decimal AverageNegativeStrengthDelta { get; set; }
    }
}
