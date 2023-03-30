using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CryptoBot.EventArgs
{
    public class ApplicationEventArgs : System.EventArgs
    {
        public DateTime SendAt { get; set; }
        public EventType Type { get; set; }
        public string Message { get; set; }
        public string MessageScope { get; set; }

        public ApplicationEventArgs(EventType type, string message, string messageScope = null)
        {
            this.SendAt = DateTime.UtcNow;
            this.Type = type;
            this.Message = message;
            this.MessageScope = messageScope;
        }

        public override string ToString()
        {
            if (String.IsNullOrEmpty(this.MessageScope))
            {
                return $"[{this.Type} ({this.SendAt})]: {this.Message}\n";
            }

            return $"{this.Message}\n";
        }
    }
}
