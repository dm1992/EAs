using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EATester.Classes
{
    public class TradingStats
    {
        public int TotalTrades { get; set; }
        public int LossTrades { get; set; }
        public int ProfitTrades { get; set; }
        public int NeutralTrades { get; set; }
    }
}
