using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TradingApp
{
    public enum EventType
    {
        INFORMATION = 0,
        ERROR = 1,
        WARNING = 2,
        STOP_TRADING = 3
    }
    public enum TradingDirection
    {
        NEUTRAL = 0,
        BUY = 1,
        WAIT_BUY = 2,
        SELL = 3,
        WAIT_SELL = 4
    }

    public enum TradingSignalStrength
    {
        NOT_DEFINED = 0,
        VERY_STRONG = 1,
        STRONG = 2,
        MEDIUM = 3,
        WEAK = 4,
        VERY_WEAK = 5
    }
}
