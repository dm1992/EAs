using MarketProxyClient.Events;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MarketProxyClient.Interfaces
{
    public interface IMarketSignalProvider : IMarketProxyClient
    {
        event EventHandler<MarketSignalEventArgs> MarketSignalEventHandler;
    }
}
