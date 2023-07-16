using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CryptoBot
{
    public enum EventType
    {
        Information = 0,
        Error = 1,
        Warning = 2,
        Debug = 3,
        TerminateApplication = 4
    }

    public enum OrderbookEventType
    {
        Unknown = 0,
        Create = 1,
        Insert = 2,
        Update = 3,
        Delete = 4
    }

    public enum MarketDirection
    {
        Unknown = 0,
        Uptrend = 1,
        Downtrend = 2,
        Neutral = 3
    }

    public enum PriceDirection
    {
        Unknown = 0,
        Up = 1,
        Down = 2,
    }

    public enum VolumeDirection
    {
        Unknown = 0,
        Buy = 1,
        Sell = 2,
    }
}
