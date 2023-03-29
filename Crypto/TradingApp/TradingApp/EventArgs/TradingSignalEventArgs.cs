using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TradingApp.Data;

namespace TradingApp.EventArgs
{
    public class TradingSignalEventArgs : System.EventArgs
    {
        public DateTime SendAt { get; set; }
        public TradingSignal TradingSignal { get; set; }

        public TradingSignalEventArgs(TradingSignal tradingSignal)
        {
            this.SendAt = DateTime.Now;
            this.TradingSignal = tradingSignal;
        }
    }
}
