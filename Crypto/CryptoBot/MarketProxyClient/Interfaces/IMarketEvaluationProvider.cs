using MarketProxyClient.Events;
using MarketProxyClient.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MarketProxyClient.Interfaces
{
    public interface IMarketEvaluationProvider : IMarketProxyClient
    {
        event EventHandler<MarketEvaluationEventArgs> MarketEvaluationEventHandler;
    }
}
