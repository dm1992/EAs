using Common;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MarketAnalyzer.Models
{
    public class MarketSignal
    {
        public string Id { get; private set; }
        public string Symbol { get; set; }
        public DateTime CreatedAt { get; set; }
        public MarketDirection MarketDirection { get; set; }
        public decimal TradingFeeAmount { get; set; }
        public decimal OpenPrice { get; set; }
        public decimal? ClosePrice { get; set; }
        public decimal TakeProfitPrice { get; set; }
        public decimal StopLossPrice { get; set; }
        public bool IsActive { get { return !this.ClosePrice.HasValue; } }
        public bool? IsForcedClosure { get; set; }
        public decimal ROI
        {
            get
            {
                if (!this.ClosePrice.HasValue)
                    return 0;

                if (this.MarketDirection == MarketDirection.Buy)
                {
                    return this.ClosePrice.Value - this.OpenPrice - this.TradingFeeAmount;
                }
                else if (this.MarketDirection == MarketDirection.Sell)
                {
                    return this.OpenPrice - this.ClosePrice.Value - this.TradingFeeAmount;
                }

                return 0;
            }
        }

        public MarketSignal(string symbol, DateTime createdAt, decimal openPrice, MarketDirection marketDirection)
        {
            this.Id = Guid.NewGuid().ToString();
            this.Symbol = symbol;
            this.CreatedAt = createdAt;
            this.OpenPrice = openPrice;
            this.MarketDirection = marketDirection;
        }

        public void SetTradingFeeAmount(decimal tradingFeeAmount)
        {
            this.TradingFeeAmount = tradingFeeAmount;
        }

        public void SetTakeProfitPrice(decimal takeProfitAmount)
        {
            if (this.MarketDirection == MarketDirection.Buy)
            {
                this.TakeProfitPrice = this.OpenPrice + takeProfitAmount;
            }
            else if (this.MarketDirection == MarketDirection.Sell)
            {
                this.TakeProfitPrice = this.OpenPrice - takeProfitAmount;
            }
        }

        public void SetStopLossPrice(decimal stopLossAmount)
        {
            if (this.MarketDirection == MarketDirection.Buy)
            {
                this.StopLossPrice = this.OpenPrice - stopLossAmount;
            }
            else if (this.MarketDirection == MarketDirection.Sell)
            {
                this.StopLossPrice = this.OpenPrice + stopLossAmount;
            }
        }

        public bool Close(decimal price, bool forceClose = false)
        {
            if (forceClose)
            {
                this.ClosePrice = price;
                this.IsForcedClosure = true;
                return true;
            }

            if (this.MarketDirection == MarketDirection.Buy)
            {
                if (price >= this.TakeProfitPrice || price <= this.StopLossPrice)
                {
                    this.ClosePrice = price;
                    return true;
                }
            }
            else if (this.MarketDirection == MarketDirection.Sell)
            {
                if (price <= this.TakeProfitPrice || price >= this.StopLossPrice)
                {
                    this.ClosePrice = price;
                    return true;
                }
            }

            return false;
        }

        public string DumpOnCreate()
        {
            return $"{this.Symbol} {this.MarketDirection} @ {this.OpenPrice} (TP = {this.TakeProfitPrice}, SL = {this.StopLossPrice}, fee = {this.TradingFeeAmount})$";
        }

        public string DumpOnClosure()
        {
            return $"{this.Symbol} {this.MarketDirection} @ {this.ClosePrice}$ ({this.ROI}$)";
        }

        public string DumpGeneralInfo()
        {
            return $"{this.Id};{this.Symbol};{this.CreatedAt};{this.MarketDirection};{this.OpenPrice};{this.ClosePrice};{this.IsForcedClosure};{this.ROI}";
        }
    }
}
