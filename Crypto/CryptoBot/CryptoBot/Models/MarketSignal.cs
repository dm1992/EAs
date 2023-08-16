using Bybit.Net.Objects.Models;
using Bybit.Net.Objects.Models.Spot.v3;
using Bybit.Net.Objects.Models.V5;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CryptoBot.Models
{
    public class MarketSignal
    {
        public string Symbol { get; set; }
        public bool IsCounter { get; set; }
        public MarketDirection MarketDirection { get; set; }
        public decimal ExitPrice { get; set; }
        public decimal EntryPrice { get; set; }
        public decimal TakeProfit
        {
            get
            {
                if (this.MarketDirection == MarketDirection.Uptrend)
                {
                    return this.EntryPrice + 32.5m;
                }
                else if (this.MarketDirection == MarketDirection.Downtrend)
                {
                    return this.EntryPrice - 32.5m;
                }

                return 0;
            }
        }
        public decimal StopLoss
        {
            get
            {
                if (this.MarketDirection == MarketDirection.Uptrend)
                {
                    return this.EntryPrice - 32.5m;
                }
                else if (this.MarketDirection == MarketDirection.Downtrend)
                {
                    return this.EntryPrice + 32.5m;
                }

                return 0;
            }
        }
        public decimal ROI
        {
            get
            {
                if (this.MarketDirection == MarketDirection.Uptrend)
                {
                    return this.ExitPrice - this.EntryPrice - 22.5m; //fee
                }
                else if (this.MarketDirection == MarketDirection.Downtrend)
                {
                    return this.EntryPrice - this.ExitPrice - 22.5m; // fee
                }

                return 0;
            }
        }

        public MarketSignal(string symbol)
        {
            this.Symbol = symbol;
        }

        public string DumpCreated()
        {
            return $"{this.Symbol} MARKET SIGNAL {(this.MarketDirection == MarketDirection.Uptrend ? "BUY" : "SELL")} @ {this.EntryPrice}$, TP = {this.TakeProfit}$, SL = {this.StopLoss}$.";
        }

        public string DumpOnRemove()
        {
            return $"!!! [{(this.ROI > 0 ? "PROFIT" : "LOSS")}] {this.Symbol} MARKET SIGNAL {(this.MarketDirection == MarketDirection.Uptrend ? "BUY" : "SELL")} @ {this.ExitPrice}$ with ROI {this.ROI}$ !!!";
        }
    }
}

