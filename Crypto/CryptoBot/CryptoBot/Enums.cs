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

    public enum MarketDirection
    {
        Unknown = 0,
        StrongUp = 1,
        StrongDown = 2,
        WeakUp = 3,
        WeakDown = 4,
        Neutral = 5
    }

    public enum MarketVolumeIntensity
    {
        Unknown = 0,
        Big = 1,
        Low = 2,
        Average = 3
    }

    public enum ManagerType
    {
        Unknown = 0,
        Davor = 1,
        Miha = 2,
        Simulation = 3
    }
}
