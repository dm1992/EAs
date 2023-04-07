using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CryptoBot.Interfaces
{
    public interface IMarket
    {
        string Id { get; }
        string Symbol { get; set; }
        DateTime CreatedAt { get; set; }
        MarketDirection GetMarketDirection();
        MarketVolumeIntensity GetMarketVolumeIntensity();
        string Dump();
    }
}
