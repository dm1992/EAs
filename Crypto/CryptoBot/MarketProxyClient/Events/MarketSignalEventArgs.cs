using Common;
using Common.Events;
using MarketProxy.Events;
using MarketProxyClient.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MarketProxyClient.Events
{
    public class MarketSignalEventArgs : BaseEventArgs
    {
        public string Symbol { get; set; }
        public MarketDirection MarketDirection { get; set; }
        public IMarketEvaluation MarketEvaluation { get; set; }

        public MarketSignalEventArgs(string symbol, MarketDirection marketDirection, IMarketEvaluation marketEvaluation) : base(MessageType.Info, "New market signal created.")
        {
            this.Symbol = symbol;
            this.MarketDirection = marketDirection;
            this.MarketEvaluation = marketEvaluation;
        }
    }
}
