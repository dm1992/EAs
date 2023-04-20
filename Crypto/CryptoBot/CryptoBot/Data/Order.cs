﻿using Bybit.Net.Objects.Models.Spot.v3;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CryptoBot.Data
{
    /// <summary>
    /// Reused Bybit spot order model
    /// </summary>
    public class Order : BybitSpotOrderV3
    {
        public decimal TakeProfitPrice { get; set; }
        public decimal StopLossPrice { get; set; }
        public decimal ExitPrice { get; set; }
        public decimal RealizedPL { get; set; }
        public bool IsActive { get; set; }

        public string Dump()
        {
            return $"{this.Id},{this.ClientOrderId},{this.Symbol},{this.Type},{this.Side},{this.UpdateTime},{this.IsActive},{this.Quantity}," +
                   $"{this.Price},{this.TakeProfitPrice},{this.StopLossPrice},{this.ExitPrice},{this.RealizedPL}";
        }
    }
}
