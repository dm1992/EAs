using Bybit.Net.Objects.Models.Socket.Spot;
using CryptoExchange.Net.Sockets;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CryptoBot.Data
{
    public abstract class Candle
    {
        public string Id { get; private set; }
        public string Symbol { get; set; }
        public DateTime CreatedAt { get; private set; }
        public bool Completed { get; set; }

        public Candle(string symbol)
        {
            this.Id = Guid.NewGuid().ToString();
            this.Symbol = symbol;
            this.CreatedAt = DateTime.Now;
            this.Completed = false;
        }

        public abstract string Dump();
    }

    public class TradeCandle : Candle
    {
        public List<DataEvent<BybitSpotTradeUpdate>> TradeBuffer { get; set; }

        public TradeCandle(string symbol) : base(symbol)
        {
            this.Symbol = symbol;
            this.TradeBuffer = new List<DataEvent<BybitSpotTradeUpdate>>();
        }

        public decimal GetTotalVolume()
        {
            if (this.TradeBuffer.IsNullOrEmpty())
                return -1;

            return this.TradeBuffer.Sum(x => x.Data.Quantity);
        }

        public decimal GetPriceMovePercentage()
        {
            if (this.TradeBuffer.IsNullOrEmpty())
                return -1;

            var highPrice = this.TradeBuffer.Max(x => x.Data.Price);
            var lowPrice = this.TradeBuffer.Min(x => x.Data.Price);

            return ((highPrice - lowPrice) / lowPrice) * 100;
        }

        public string DumpTrades()
        {
            string result = $"Total {this.TradeBuffer.Count} trades in candle with Id {this.Id}:\n";

            foreach (var trade in this.TradeBuffer.OrderByDescending(x => x.Data.Timestamp))
            {
                result += $"{trade.Data.Timestamp},{(trade.Data.Buy ? "BUY" : "SELL")},{trade.Data.Price},{trade.Data.Quantity}\n";
            }

            return result;
        }

        public override string Dump()
        {
            return $"{this.CreatedAt}, {this.Symbol} candle (completed: {this.Completed}) with volume: {this.GetTotalVolume()} and price move percentage: {this.GetPriceMovePercentage()} %.";
        }
    }

    public class PriceClosureCandle : Candle
    {
        public List<PriceClosure> PriceClosures { get; set; }

        public PriceClosureCandle(string symbol) : base(symbol)
        {
            this.Symbol = symbol;
            this.PriceClosures = new List<PriceClosure>();
        }

        public decimal GetPriceMove()
        {
            if (this.PriceClosures.IsNullOrEmpty())
                return -1;

            var orderedPriceClosures = this.PriceClosures.OrderBy(x => x.CreatedAt);
            return orderedPriceClosures.First().ClosePrice - orderedPriceClosures.Last().ClosePrice;
        }

        public decimal GetAverageBuyerVolume()
        {
            if (this.PriceClosures.IsNullOrEmpty())
                return -1;

            return this.PriceClosures.Where(x => x.BuyerVolume > 0).Average(x => x.BuyerVolume);
        }

        public decimal GetAverageSellerVolume()
        {
            if (this.PriceClosures.IsNullOrEmpty())
                return -1;

            return this.PriceClosures.Where(x => x.SellerVolume > 0).Average(x => x.SellerVolume);
        }

        public override string Dump()
        {
            return $"{this.Symbol},{this.CreatedAt},{this.GetPriceMove()},{this.GetAverageBuyerVolume()},{this.GetAverageSellerVolume()}";
        }
    }
}
