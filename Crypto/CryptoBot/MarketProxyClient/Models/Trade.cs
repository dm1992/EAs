using Common;
using MarketProxyClient;
using MarketProxyClient.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MarketProxyClient.Models
{
    public class Trade : ITrade
    {
        public Trade()
        {
            this.Id = Guid.NewGuid().ToString();
            this.CreatedAt = DateTime.Now;
        }

        public string Id { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? ClosedAt { get; set; }
        public TimeSpan? Duration 
        {
            get
            {
                if (!this.ClosedAt.HasValue)
                    return null;

                return (this.ClosedAt.Value - this.CreatedAt).Duration();
            }
        }
        public string Symbol { get; set; }
        public MarketDirection MarketDirection { get; set; }
        public IMarketEvaluation MarketEvaluation { get; set; }
        public decimal TakeProfit { get; set; }
        public decimal StopLoss { get; set; }
        public decimal OpenPrice { get; set; }
        public decimal? ClosePrice { get; set; }
        public decimal? FeeRate { get; set; }
        public bool IsActive 
        { 
            get
            {
                return !this.ClosePrice.HasValue;
            }
        }
        public decimal Balance
        {
            get
            {
                if (!this.ClosePrice.HasValue)
                    return 0;

                if (this.MarketDirection == MarketDirection.Buy)
                {
                    return this.ClosePrice.Value - this.OpenPrice;
                }
                else if (this.MarketDirection == MarketDirection.Sell)
                {
                    return this.OpenPrice - this.ClosePrice.Value;
                }

                return 0;
            }
        }

        public string Dump()
        {
            if (!this.ClosedAt.HasValue)
            {
                return $"{this.Id};{this.CreatedAt};{this.Symbol};{this.MarketDirection};{this.OpenPrice};{this.MarketEvaluation.Dump()}";
            }

            return $"{this.Id};{this.Duration};{this.Symbol};{this.MarketDirection};{this.ClosePrice};{this.Balance}";
        }
    }
}
