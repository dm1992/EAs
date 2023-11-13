using MarketProxy.Events;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MarketClient.Interfaces.Socket
{
    public interface IPriceSocket : ISocket
    {
        event EventHandler<PriceEventArgs> PriceEventHandler;

        void SubscribeToPricesAsync(IEnumerable<string> symbols);
    }
}
