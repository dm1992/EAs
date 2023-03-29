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
        decimal AverageMinuteBuyersVolume { get; }
        decimal AverageMinuteSellersVolume { get; }
        decimal AverageMinutePriceMovePercentage { get; }
        string Dump();
    }

    public interface IAggressiveMarket : IMarket
    {
        decimal BuyersVolume { get; }
        decimal SellersVolume { get; }
    }

    public interface IPassiveMarket : IMarket
    {
        decimal BuyersVolume { get; }
        decimal SellersVolume { get; }
    }
}
