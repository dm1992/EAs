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
        public string Symbol { get;  set; }
        public OrderOperation Operation { get;  set; }
        public double Lots { get; set; }
        public double EntryPrice { get; set; }
        public double StopLoss { get; set; }
        public double TakeProfit { get; set; }
        public string Comment { get; set; } = "Test";
        public int MagicNumber { get; set; } = 159357;
        public DateTime ActiveTo { get; set; } = DateTime.MinValue;
        public Color Color { get; set; }
        public bool BreakEvenReached { get; set; } = false;
        public bool PendingModify { get; set; } = false;
    }

    public enum OrderOperation
    {
        NONE = -1,
        BUY = 0,
        SELL = 1,
        BUY_LIMIT = 2,
        SELL_LIMIT = 3,
        BUY_STOP = 4,
        SELL_STOP = 5
    }
}
