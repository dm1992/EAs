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
}
