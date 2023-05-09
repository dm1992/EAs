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
        Uptrend = 1,
        Downtrend = 2,
        Neutral = 3
    }

    public enum MarketVolumeIntensity
    {
        Unknown = 0,
        BigBuyers = 1,
        BigSellers = 2
    }

    public enum MarketSpectatorMode
    {
        Unknown = 0,
        ExecutedTrades_MicroLevel = 1,
        ExecutedTrades_MacroLevel = 2,
        ElapsedTime_MicroLevel = 3,
        ElapsedTime_MacroLevel = 4,
    }

    public enum ManagerType
    {
        Unknown = 0,
        Davor = 1,
        Miha = 2,
        Simulation = 3
    }
}
