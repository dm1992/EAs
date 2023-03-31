using Bybit.Net.Objects.Models.Socket.Spot;
using CryptoExchange.Net.Sockets;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CryptoBot.Data
{
    public class CandleBatch
    {
        public string Id { get; private set; }
        public string Symbol { get; set; }
        public DateTime CreatedAt { get; private set; }
        public bool Completed
        {
            get
            {
                if (this.Candles.IsNullOrEmpty())
                    return false;

                return this.Candles.All(x => x.Completed);
            }
        }
        public List<Candle> Candles { get; set; }

        public CandleBatch(string symbol)
        {
            this.Id = Guid.NewGuid().ToString();
            this.Symbol = symbol;
            this.CreatedAt = DateTime.Now;
            this.Candles = new List<Candle>();
        }

        public decimal GetAverageVolume()
        {
            if (this.Candles.IsNullOrEmpty())
                return -1;

            return this.Candles.Average(x => x.GetVolume());
        }

        public decimal GetAveragePriceMovePercentage()
        {
            if (this.Candles.IsNullOrEmpty())
                return -1;

            return this.Candles.Average(x => x.GetPriceMovePercentage());
        }

        public string Dump(bool generalInfo = true)
        {
            if (generalInfo)
            {
                return $"({this.Id}) {this.Symbol} candle batch (completed: {this.Completed}). Total trades in candle batch: {this.Candles.Sum(x => x.TradeBuffer.Count)}.";
            }

            return $"{this.CreatedAt},{this.Id},{this.GetAverageVolume()},{this.GetAveragePriceMovePercentage()}";
        }
    }

    public class Candle
    {
        public string Id { get; private set; }
        public string Symbol { get; set; }
        public DateTime CreatedAt { get; private set; }
        public bool Completed { get; set; }
        public List<DataEvent<BybitSpotTradeUpdate>> TradeBuffer { get; set; }

        public Candle(string symbol)
        {
            this.Id = Guid.NewGuid().ToString();
            this.Symbol = symbol;
            this.CreatedAt = DateTime.Now;
            this.Completed = false;
            this.TradeBuffer = new List<DataEvent<BybitSpotTradeUpdate>>();          
        }

        public decimal GetVolume()
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

        public string Dump()
        {
            return $"{this.CreatedAt}, {this.Id}, {this.Symbol} candle (completed: {this.Completed}) with volume: {this.GetVolume()} and price move percentage: {this.GetPriceMovePercentage()} %.";
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
    } 
}
