using Bybit.Net.Enums;
using Bybit.Net.Objects.Models.Spot.v3;
using Bybit.Net.Objects.Models.V5;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CryptoBot.Models
{
    public class Order : BybitOrder
    {
        public decimal? LastPrice { get; set; }
        public decimal TakeProfitPrice { get; set; }
        public decimal StopLossPrice { get; set; }
        public decimal ExitPrice { get; set; }
        public decimal RealizedProfitLossAmount { get; set; }
        public bool IsActive { get; set; }
        public bool MustFinish 
        {
            get
            {
                if (!this.LastPrice.HasValue || this.LastPrice.Value == this.Price) 
                    return false;

                if (this.Side == OrderSide.Buy)
                {
                    return this.LastPrice >= this.TakeProfitPrice || this.LastPrice <= this.StopLossPrice;
                }
                else if (this.Side == OrderSide.Sell)
                {
                    return this.LastPrice <= this.TakeProfitPrice || this.LastPrice >= this.StopLossPrice;
                }

                return false;
            }
        }

        public string Dump()
        {
            return $"{this.OrderId},{this.ClientOrderId},{this.Symbol},{this},{this.Side},{this.CreateTime},{this.IsActive},{this.Quantity}," +
                   $"{this.Price},{this.TakeProfitPrice},{this.StopLossPrice},{this.ExitPrice},{this.RealizedProfitLossAmount}";
        }
    }
}
