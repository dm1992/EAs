using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CryptoBot.EventArgs
{
    public class MarketManagerEventArgs : ApplicationEventArgs
    {
        public override string EventTag => "MarketManager";

        public MarketManagerEventArgs(EventType eventType, string message, string messageScope = null) : base(eventType, message, messageScope)
        {

        }

        public override string Dump()
        {
            return $"[{this.EventTag} {this.EventType} ({this.SendAt})]: {this.Message}\n";
        }
    }
}
