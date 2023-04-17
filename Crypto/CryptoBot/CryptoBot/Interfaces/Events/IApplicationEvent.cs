using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CryptoBot.EventArgs;

namespace CryptoBot.Interfaces.Events
{
    public interface IApplicationEvent
    {
        event EventHandler<ApplicationEventArgs> ApplicationEvent;
    }
}
