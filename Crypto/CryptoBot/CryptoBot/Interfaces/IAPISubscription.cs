using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CryptoBot.Interfaces
{
    public interface IAPISubscription : IApplicationEvent
    {
        void InvokeAPISubscription();
        void CloseAPISubscription();
    }
}
