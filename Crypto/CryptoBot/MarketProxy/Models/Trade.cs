using Common;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MarketProxy.Models
{
    public class Trade
    {
        public string Id { get; set; }
        public string Symbol { get; set; }
        public TradeDirection TradeDirection { get; set; }
        public DateTime Time { get; set; }
        public decimal Price { get; set; }
        public decimal DeltaPrice { get; set; }
        public decimal Volume { get; set; }

        public string Dump(bool minimize = false)
        {
            if (minimize)
            {
                return $"{this.TradeDirection};{this.Time:dd.MM.yyyy HH:mm:ss};{this.Price};{this.Volume}";
            }

            return $"{this.Id};{this.Symbol};{this.TradeDirection};{this.Time:dd.MM.yyyy HH:mm:ss};{this.Price};{this.DeltaPrice};{this.Volume}";
        }

        public string DumpV2()
        {
            return $"{this.Symbol};{this.Time:dd.MM.yyyy HH:mm:ss};{this.Price};{this.Volume};{this.TradeDirection}";
        }
    }
}
