using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EA
{
    public static class Helpers
    {
        public static double FindMax(List<double> list)
        {
            if (list == null || !list.Any())
                return 0;

            return list.Max();
        }

        public static double FindMin(List<double> list)
        {
            if (list == null || !list.Any())
                return 0;

            return list.Min();
        }
    }
}
