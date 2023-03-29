using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TradingApp.Data
{
    public class TradingSignal
    {
        public long InternalId { get; set; }
        public string Symbol { get; set; }
        public TradingDirection Direction { get; set; }
        public TradingSignalStrength Strength { get; set; }
        public decimal TakeProfit { get; set; }
        public decimal StopLoss { get; set; }

        public int RealizationAttempts { get; set; }
        public bool MarkAsForgotten { get; set; }

        public TradingSignal()
        {
            this.MarkAsForgotten = false;
        }

        public override string ToString()
        {
            return $"{this.Direction} TRADING SIGNAL!!!";
        }
    }
}
