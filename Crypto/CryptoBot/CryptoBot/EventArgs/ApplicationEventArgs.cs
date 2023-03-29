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
        public string CryptoCurrency { get; set; }
        public bool Verbose { get; set; }

        public ApplicationEventArgs(EventType type, string message, string cryptoCurrency = null, bool verbose = false)
        {
            this.SendAt = DateTime.UtcNow;
            this.Type = type;
            this.Message = message;
            this.CryptoCurrency = cryptoCurrency;
            this.Verbose = verbose;
        }

        public override string ToString()
        {
            return $"[{this.Type} ({this.SendAt})]: {this.Message}\n";
        }
    }
}
