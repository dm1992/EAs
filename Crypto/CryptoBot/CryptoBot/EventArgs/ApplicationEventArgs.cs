using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CryptoBot.EventArgs
{
    public abstract class ApplicationEventArgs : System.EventArgs
    {
        public DateTime SendAt { get; set; }
        public EventType EventType { get; set; }
        public abstract string  EventTag { get; }
        public string Message { get; set; }
        public string MessageScope { get; set; }

        public ApplicationEventArgs(EventType eventType, string message, string messageScope = null)
        {
            this.SendAt = DateTime.UtcNow;
            this.EventType = eventType;
            this.Message = message;
            this.MessageScope = messageScope;
        }

        public abstract string Dump();
    }

    public class MarketManagerEventArgs : ApplicationEventArgs
    {
        public override string EventTag => "MarketManager";

        public MarketManagerEventArgs(EventType eventType, string message, string messageScope = null) : base (eventType, message, messageScope)
        {

        }

        public override string Dump()
        {
            return $"[{this.EventTag} {this.EventType} ({this.SendAt})]: {this.Message}\n";
        }
    }

    public class OrderManagerEventArgs : ApplicationEventArgs
    {
        public override string EventTag => "OrderManager";

        public OrderManagerEventArgs(EventType eventType, string message, string messageScope = null) : base(eventType, message, messageScope)
        {
            // for now only default params
        }

        public override string Dump()
        {
            return $"[{this.EventTag} {this.EventType} ({this.SendAt})]: {this.Message}\n";
        }
    }

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
