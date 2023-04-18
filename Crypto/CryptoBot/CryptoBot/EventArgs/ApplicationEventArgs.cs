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
        public abstract string EventTag { get; }
        public EventType EventType { get; set; }
        public string Message { get; set; }
        public bool Verbose { get; set; }

        public ApplicationEventArgs(EventType eventType, string message, bool verbose = false)
        {
            this.SendAt = DateTime.UtcNow;
            this.EventType = eventType;
            this.Message = message;
            this.Verbose = verbose;
        }

        public abstract string Dump();
    }

    public class MarketManagerEventArgs : ApplicationEventArgs
    {
        public override string EventTag => "MarketManager";

        public MarketManagerEventArgs(EventType eventType, string message, bool verbose = false) : base (eventType, message, verbose)
        {

        }

        public override string Dump()
        {
            return $"[{this.EventType} ({this.SendAt})]: {this.Message}\n";
        }
    }

    public class OrderManagerEventArgs : ApplicationEventArgs
    {
        public override string EventTag => "OrderManager";

        public OrderManagerEventArgs(EventType eventType, string message, bool verbose = false) : base(eventType, message, verbose)
        {
            // for now only default params
        }

        public override string Dump()
        {
            return $"[{this.EventType} ({this.SendAt})]: {this.Message}\n";
        }
    }

    public class TradingManagerEventArgs : ApplicationEventArgs
    {
        public override string EventTag => "TradingManager";

        public TradingManagerEventArgs(EventType eventType, string message, bool verbose = false) : base(eventType, message, verbose)
        {
            // for now only default params
        }

        public override string Dump()
        {
            return $"[{this.EventType} ({this.SendAt})]: {this.Message}\n";
        }
    }
}
