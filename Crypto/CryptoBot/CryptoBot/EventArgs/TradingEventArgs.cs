using System;
using Bybit.Net.Objects.Models.Spot.v1;

namespace CryptoBot.EventArgs
{
    public class TradingEventArgs : System.EventArgs
    {
        public DateTime SendAt { get; set; }
        public BybitSpotOrderV1 Order { get; set; }

        public TradingEventArgs(BybitSpotOrderV1 order)
        {
            this.SendAt = DateTime.UtcNow;
            this.Order = order;
        }
    }
}
