using Bybit.Net.Objects.Models.Socket.Spot;
using CryptoExchange.Net.Sockets;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CryptoBot.Data
{
    public abstract class CandleBatch
    {
        public string Id { get; private set; }
        public string Symbol { get; set; }
        public DateTime CreatedAt { get; private set; }
        public abstract bool Completed { get; }

        public CandleBatch(string symbol)
        {
            this.Id = Guid.NewGuid().ToString();
            this.Symbol = symbol;
            this.CreatedAt = DateTime.Now;
        }

        public abstract string Dump();
    }

    public class TradeCandleBatch : CandleBatch
    {
        public List<TradeCandle> TradeCandles { get; set; }
        public override bool Completed
        {
            get
            {
                if (this.TradeCandles.IsNullOrEmpty())
                    return false;

                return this.TradeCandles.All(x => x.Completed);
            }
        }

        public TradeCandleBatch(string symbol) : base(symbol)
        {
            this.Symbol = symbol;
            this.TradeCandles = new List<TradeCandle>();
        }

        public decimal GetAverageVolume()
        {
            if (this.TradeCandles.IsNullOrEmpty())
                return -1;

            return this.TradeCandles.Average(x => x.GetTotalVolume());
        }

        public decimal GetAveragePriceMovePercentage()
        {
            if (this.TradeCandles.IsNullOrEmpty())
                return -1;

            return this.TradeCandles.Average(x => x.GetPriceMovePercentage());
        }

        public override string Dump()
        {
            return $"{this.Symbol},{this.CreatedAt},{this.GetAverageVolume()},{this.GetAveragePriceMovePercentage()}";
        }
    }

    public class PriceClosureCandleBatch : CandleBatch
    {
        public List<PriceClosureCandle> PriceClosureCandles { get; set; }
        public override bool Completed { get; }

        public PriceClosureCandleBatch(string symbol) : base(symbol)
        {
            this.Symbol = symbol;
            this.PriceClosureCandles = new List<PriceClosureCandle>();
        }

        public decimal GetLatestPriceMove()
        {
            if (this.PriceClosureCandles.IsNullOrEmpty())
                return -1;

            return this.PriceClosureCandles.OrderByDescending(x => x.CreatedAt).First().GetPriceMove();
        }

        public decimal GetPositiveAveragePriceMove()
        {
            if (this.PriceClosureCandles.IsNullOrEmpty())
                return -1;

            return this.PriceClosureCandles.Where(x => x.GetPriceMove() > 0).Average(x => x.GetPriceMove());
        }

        public decimal GetNegativeAveragePriceMove()
        {
            if (this.PriceClosureCandles.IsNullOrEmpty())
                return -1;

            return this.PriceClosureCandles.Where(x => x.GetPriceMove() < 0).Average(x => x.GetPriceMove());
        }

        public decimal GetTotalAverageBuyerVolume()
        {
            if (this.PriceClosureCandles.IsNullOrEmpty())
                return -1;

            return this.PriceClosureCandles.Average(x => x.GetAverageBuyerVolume());
        }

        public decimal GetTotalAverageSellerVolume()
        {
            if (this.PriceClosureCandles.IsNullOrEmpty())
                return -1;

            return this.PriceClosureCandles.Average(x => x.GetAverageSellerVolume());
        }

        public List<PriceClosure> GetTotalPriceClosures()
        {
            if (this.PriceClosureCandles.IsNullOrEmpty())
                return null;

            List<PriceClosure> priceClosures = new List<PriceClosure>();
            foreach (var priceClosureCandle in this.PriceClosureCandles)
            {
                priceClosures.Concat(priceClosureCandle.PriceClosures);
            }

            return priceClosures;
        }

        public override string Dump()
        {
            return $"{this.Symbol},{this.CreatedAt},{this.GetPositiveAveragePriceMove()},{this.GetNegativeAveragePriceMove()},{this.GetTotalAverageBuyerVolume()},{this.GetTotalAverageSellerVolume()}";
        }
    }
}
