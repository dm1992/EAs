using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TradingApp.EventArgs;

namespace TradingApp.Interfaces
{
    public interface IApplicationEvent
    {
        /// <summary>
        /// General application event.
        /// </summary>
        event EventHandler<ApplicationEventArgs> ApplicationEvent;
    }
}
