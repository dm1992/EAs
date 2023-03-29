using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TradeCollectorApp.Managers
{
    public interface IAPIManager
    {
        bool TradeCollectFinished { get; }

        void SubscribeToTradeUpdatesAsync();
    }
}
