using Bybit.Net.Objects.Models.Socket.Spot;
using CryptoExchange.Net.Sockets;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CryptoBot.Data
{
    public class PriceLevelClosure
    {
        public PriceLevelClosure (string symbol, List<DataEvent<BybitSpotTradeUpdate>> trades, decimal latestSymbolPrice)
        {
            this.Id = Guid.NewGuid().ToString();
            this.Symbol = symbol;
            this.CreatedAt = DateTime.UtcNow;
            this.Trades = trades;
            this.LatestSymbolPrice = latestSymbolPrice;
        }

        public string Id { get; set; }
        public string Symbol { get; set; }
        public DateTime CreatedAt { get; private set; }
        public List<DataEvent<BybitSpotTradeUpdate>> Trades { get; set; }

        public decimal PriceLevel 
        {
            get
            {
                if (this.Trades.IsNullOrEmpty())
                    return -1;

                return this.Trades.First().Data.Price;
            }
        }

        public decimal LatestSymbolPrice { get; set; }

        public decimal BuyerQuantity
        {
            get
            {
                if (this.Trades.IsNullOrEmpty())
                    return -1;

                return this.Trades.Where(x => x.Data.Buy).Sum(x => x.Data.Quantity);
            }
        }

        public decimal SellerQuantity
        {
            get
            {
                if (this.Trades.IsNullOrEmpty())
                    return -1;

                return this.Trades.Where(x => !x.Data.Buy).Sum(x => x.Data.Quantity);
            }
        }

        public string Dump()
        {
            return $"{Symbol} closure price level: {PriceLevel}, LatestSymbolPrice: {LatestSymbolPrice}, BuyerQuantity: {BuyerQuantity}, SellerQuantity: {SellerQuantity}, Trades: {this.Trades.Count()}.";
        }
    }
}
