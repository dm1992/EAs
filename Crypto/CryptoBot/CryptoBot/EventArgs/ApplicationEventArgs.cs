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
        public abstract string MessageTag { get; }
        public string MessageSubTag { get; set; }
        public string Message { get; set; }

        public ApplicationEventArgs(EventType eventType, string message, string messageSubTag = null)
        {
            this.SendAt = DateTime.UtcNow;
            this.EventType = eventType;
            this.Message = message;
            this.MessageSubTag = messageSubTag;
        }

        public abstract string Dump();
    }

    public class MarketManagerEventArgs : ApplicationEventArgs
    {
        public override string MessageTag => "MarketManager";

        public MarketManagerEventArgs(EventType eventType, string message, string messageSubTag = null) : base (eventType, message, messageSubTag)
        {

        }

        public override string Dump()
        {
            return $"[{this.MessageTag} {this.EventType} ({this.SendAt})]: {this.Message}\n";
        }
    }

    public class OrderManagerEventArgs : ApplicationEventArgs
    {
        public override string MessageTag => "OrderManager";

        public OrderManagerEventArgs(EventType eventType, string message, string messageSubTag = null) : base(eventType, message, messageSubTag)
        {
            // for now only default params
        }

        public override string Dump()
        {
            return $"[{this.MessageTag} {this.EventType} ({this.SendAt})]: {this.Message}\n";
        }
    }

    public class TradingManagerEventArgs : ApplicationEventArgs
    {
        public override string MessageTag => "TradingManager";

        public TradingManagerEventArgs(EventType eventType, string message, string messageSubTag = null) : base(eventType, message, messageSubTag)
        {
            // for now only default params
        }

        public override string Dump()
        {
            return $"[{this.MessageTag} {this.EventType} ({this.SendAt})]: {this.Message}\n";
        }
    }
}
