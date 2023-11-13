using Common;
using Common.Events;
using MarketProxy.Events;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MarketProxy.Events
{
    public class UnsolicitedEventArgs : BaseEventArgs
    {
        public UnsolicitedEventArgs(MessageType messageType, string message) : base(messageType, message)
        {

        }
    }
}
