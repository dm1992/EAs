using CryptoBot.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CryptoBot.Data.Miha
{
    public class Market : IMarket
    {
        public string Id => throw new NotImplementedException();

        public string Symbol { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
        public DateTime CreatedAt { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }

        public decimal AverageMinuteBuyersVolume => throw new NotImplementedException();

        public decimal AverageMinuteSellersVolume => throw new NotImplementedException();

        public decimal AverageMinutePriceMovePercentage => throw new NotImplementedException();

        public string Dump()
        {
            throw new NotImplementedException();
        }

        public MarketDirection GetMarketDirection()
        {
            throw new NotImplementedException();
        }
    }
}
