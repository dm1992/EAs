using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MarketAnalyzer.Models
{
    public class DrawingResult
    {
        public DateTime Date { get; set; }

        public List<int> Numbers1 { get; set; }

        public List<int> Numbers2 { get; set; }
    }
}
