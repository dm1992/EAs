using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EATester.Classes
{
    public class Order
    {
        public int Type { get; set; }
        public int Ticket { get; set; }
        public string Name { get; set; }
        public OrderOperation Operation { get; set; }
        public double Price { get; set; }
        public double SL { get; set; }
        public double TP { get; set; }
        public int Bars { get; set; }
        public double OriginalLots { get; set; }
        public double Lots { get; set; }
        public double ProfitPips { get; set; }
        public string Comment { get; set; } = "Test";
        public DateTime Expiration { get; set; }
        public DateTime ActiveTo { get; set; } = DateTime.MinValue;
        public DateTime OpenTime { get; set; } = DateTime.MinValue;
        public double ClosePrice { get; set; }
        public Dictionary<double, double> ClosureRates { get; set; } = new Dictionary<double, double>();
    }

    public enum OrderOperation
    {
        Unknown = -1,
        Buy = 0,
        Sell = 1,
        BuyLimit = 2,
        SellLimit = 3,
        BuyStop = 4,
        SellStop = 5
    }
}
