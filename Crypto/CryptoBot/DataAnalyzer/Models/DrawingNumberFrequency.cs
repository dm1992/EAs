using Common;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MarketAnalyzer.Models
{
    public class DrawingNumberFrequency
    {
        public LotteryResultFilter LotteryResultFilter { get; set; }
        public int Value { get; set; }
        public List<KeyValuePair<int, int>> NumberFrequency1 { get; set; }
        public List<KeyValuePair<int, int>> NumberFrequency2 { get; set; }

        public string Dump()
        {
            string result = "";

            if (!this.NumberFrequency1.IsNullOrEmpty())
            {
                foreach (var nf in this.NumberFrequency1)
                {
                    result += $"{nf.Key} : {nf.Value}\n";
                }
            }

            if (!this.NumberFrequency2.IsNullOrEmpty())
            {
                result += "-------------------\n";

                foreach (var nf in this.NumberFrequency2)
                {
                    result += $"{nf.Key} : {nf.Value}\n";
                }
            }

            return result;
        }
    }
}
