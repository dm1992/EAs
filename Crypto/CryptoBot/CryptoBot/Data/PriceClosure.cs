using Bybit.Net.Objects.Models.Socket.Spot;
using CryptoExchange.Net.Sockets;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CryptoBot.Data
{
    public class PriceClosure
    {
        public string Id { get; set; }
        public string Symbol { get; set; }
        public DateTime CreatedAt { get; private set; }
        public List<DataEvent<BybitSpotTradeUpdate>> Trades { get; set; }
        public decimal LatestPrice { get; set; }
        public decimal ClosePrice
        {
            get
            {
                if (this.Trades.IsNullOrEmpty())
                    return -1;

                return this.Trades.First().Data.Price;
            }
        }
        public decimal BuyerVolume
        {
            get
            {
                if (this.Trades.IsNullOrEmpty())
                    return -1;

                return this.Trades.Where(x => x.Data.Buy).Sum(x => x.Data.Quantity);
            }
        }
        public decimal SellerVolume
        {
            get
            {
                if (this.Trades.IsNullOrEmpty())
                    return -1;

                return this.Trades.Where(x => !x.Data.Buy).Sum(x => x.Data.Quantity);
            }
        }

        public PriceClosure(string symbol, decimal latestPrice, List<DataEvent<BybitSpotTradeUpdate>> trades)
        {
            this.Id = Guid.NewGuid().ToString();
            this.Symbol = symbol;
            this.CreatedAt = DateTime.UtcNow;
            this.Trades = trades;
            this.LatestPrice = latestPrice;
        }

        public string Dump()
        {
            return $"{this.CreatedAt},{this.Symbol},{this.ClosePrice},{this.LatestPrice},{this.BuyerVolume},{this.SellerVolume},{this.Trades.Count()}";
        }
    }
}
