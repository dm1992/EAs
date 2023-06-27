using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CryptoBot.EventArgs
{
    public class TradingManagerEventArgs : ApplicationEventArgs
    {
        public override string EventTag => "TradingManager";

        public TradingManagerEventArgs(EventType eventType, string message, string messageScope = null) : base(eventType, message, messageScope)
        {
            // for now only default params
        }

        public override string Dump()
        {
            return $"[{this.EventTag} {this.EventType} ({this.SendAt})]: {this.Message}\n";
        }
    }
}
