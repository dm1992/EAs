using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CryptoBot.EventArgs;

namespace CryptoBot.Interfaces
{
    public interface IApplicationEvent
    {
        /// <summary>
        /// General application event.
        /// </summary>
        event EventHandler<ApplicationEventArgs> ApplicationEvent;
    }
}
