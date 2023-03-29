using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EA.Data
{
    public class Order
    {
        public int TicketId { get; set; } = -1;
        public string SymbolName { get;  set; }
        public OrderOperation Operation { get;  set; }
        public double OriginalLots { get; set; }
        public double Lots { get; set; }
        public double EntryPrice { get; set; }
        public double StopLoss { get; set; }
        public double TakeProfit { get; set; }
        public double ProfitPips { get; set; }
        public string Comment { get; set; } = "Test";
        public int MagicNumber { get; set; } = 159357;
        public DateTime ActiveTo { get; set; } = DateTime.MinValue;
        public DateTime OpenTime { get; set; } = DateTime.MinValue;
        public Color Color { get; set; }
        public Dictionary<double, double> ClosureRates { get; set; } = new Dictionary<double, double>();

        public override string ToString()
        {
            return $"TicketId: {TicketId}, SymbolName: {SymbolName}, Operation: {Operation}, Lots: {Lots}, OriginalLots: {OriginalLots}, EntryPrice: {EntryPrice}, StopLoss: {StopLoss}, TakeProfit: {TakeProfit}, ProfitPips: {ProfitPips}, ClosureRates: {string.Join(",", ClosureRates.Select(kv => kv.Key + ":" + kv.Value).ToArray())}.";
        }
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
