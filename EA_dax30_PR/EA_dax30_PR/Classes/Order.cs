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
        public double Price { get; set; }
        public double SL { get; set; }
        public double TP { get; set; }
        public int Bars { get; set; }
    }
}
