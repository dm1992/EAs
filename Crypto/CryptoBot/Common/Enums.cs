using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Common
{
    public enum WallEffect
    {
        Wait = 0,
        Buy = 1,
        Sell = 2
    }

    public enum ImpulseEffect
    {
        Wait = 0,
        Buy = 1,
        Sell = 2
    }

    public enum MarketDirection
    {
        Wait = 0,
        Buy = 1,
        Sell = 2
    }

    public enum MessageType
    {
        Info = 0,
        Warning = 1,
        Error = 2
    }
    public enum TradeDirection
    {
        Buy = 0,
        Sell = 1
    }

    public enum LotteryResultFilter
    {
        None = 0,
        Year = 1,
        Month = 2
    }
}
