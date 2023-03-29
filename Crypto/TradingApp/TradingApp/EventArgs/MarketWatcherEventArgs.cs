using Bybit.Net.Objects.Models.Socket.Spot;
using CryptoExchange.Net.Sockets;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TradingApp.Data;

namespace TradingApp.EventArgs
{
    public class MarketWatcherEventArgs : System.EventArgs
    {
        public DateTime SendAt { get; set; }
        public Candle Candle { get; set; }

        public MarketWatcherEventArgs(Candle candle)
        {
            this.SendAt = DateTime.Now;
            this.Candle = candle;
        }
    }
}
