using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TradingApp.EventArgs
{
    public class ApplicationEventArgs
    {
        public DateTime SendAt { get; set; }
        public EventType Type { get; set; }
        public string Message { get; set; }

        public ApplicationEventArgs(EventType type, string message)
        {
            this.SendAt = DateTime.Now;
            this.Type = type;
            this.Message = message;
        }

        public override string ToString()
        {
            return $"({this.SendAt}) [{this.Type}]: {this.Message}";
        }
    }
}
