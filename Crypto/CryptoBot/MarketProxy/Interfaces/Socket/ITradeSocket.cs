using MarketProxy.Events;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MarketClient.Interfaces.Socket
{
    public interface ITradeSocket : ISocket
    {
        event EventHandler<TradeEventArgs> TradeEventHandler;

        event EventHandler<OrderbookEventArgs> OrderbookEventHandler;

        void SubscribeToTradesAsync(IEnumerable<string> symbols);

        void SubscribeToOrderbookAsync(IEnumerable<string> symbols, int depth);
    }
}
