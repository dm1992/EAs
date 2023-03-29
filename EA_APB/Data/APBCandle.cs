using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EA.Data
{
    /// <summary>
    /// APB candle is special sort of regular candles with averaging price applied.
    /// </summary>
    public class APBCandle
    {
        public double Open { get; set; }
        public double High { get; set; }
        public double Low { get; set; }
        public double Close { get; set; }
        public CandleColor Color { get; set; }
    }

    public enum CandleColor
    {
        NONE,
        SELL,
        BUY
    }
}
