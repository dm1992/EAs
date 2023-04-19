using Bybit.Net.Objects.Models.Spot.v3;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CryptoBot.Data
{
    /// <summary>
    /// Reused of Bybit spot order model
    /// </summary>
    public class Order : BybitSpotOrderV3
    {
        public decimal TakeProfitPrice { get; set; }
        public decimal StopLossPrice { get; set; }

        public string Dump()
        {
            return $"'{this.Id}' ('{this.ClientOrderId}'), '{this.Symbol}', '{this.Price}', '{this.Quantity}', " +
            $"'{this.Type}', '{this.Side}', '{this.Status}', '{this.TimeInForce}', " +
            $"'{this.QuantityFilled}', '{this.QuoteQuantity}', '{this.AveragePrice}', '{this.StopPrice}', " +
            $"'{this.IcebergQuantity}', '{this.CreateTime}', '{this.UpdateTime}', '{this.IsWorking}', '{this.TakeProfitPrice}', '{this.StopLossPrice}'";
        }
    }
}
