using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EA.Data
{
    public class TradingSymbol
    {
        public string Name { get; set; }
        public List<TimeInterval> TimeIntervals { get; set; }
        
        public TradingSymbol(string name)
        {
            Name = name;
            TimeIntervals = new List<TimeInterval>();

        }

        public void PrepareTimeIntervals()
        {
            //xxx add this to config
            if (this.Name == "EURUSD")
            {
                TimeInterval t1 = new TimeInterval();
                t1.ParseTimeInterval("9:10-17:55");
                //TimeInterval t2 = new TimeInterval();
                //t2.ParseTimeInterval("15:10-16:55");

                TimeIntervals.Add(t1);
                //TimeIntervals.Add(t2);
            }
            //else if (this.Name == "US30")
            //{
            //    TimeInterval t1 = new TimeInterval();
            //    t1.ParseTimeInterval("15:10-19:55");

            //    TimeIntervals.Add(t1);
            //}
        }

        public override string ToString()
        {
            return $"Name: {Name}";
        }

    }

    public class TimeInterval
    {
        public int FromHour { get; set; }
        public int FromMinute { get; set; }
        public int ToHour { get; set; }
        public int ToMinute { get; set; }

        public bool ParseTimeInterval(string data)
        {
            try
            {
                int timePart = 0;
                foreach (string timeData in data.Split('-'))
                {
                    string[] t = timeData.Split(':');
                    if (timePart == 0)
                    {
                        this.FromHour = Convert.ToInt32(t[0]);
                        this.FromMinute = Convert.ToInt32(t[1]);
                    }
                    else if (timePart == 1)
                    {
                        this.ToHour = Convert.ToInt32(t[0]);
                        this.ToMinute = Convert.ToInt32(t[1]);
                    }

                    timePart++;
                }

                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        public override string ToString()
        {
            return $"TimeInterval data. FromHour: {FromHour}, FromMinute: {FromMinute} | ToHour: {ToHour}, ToMinute: {ToMinute}.";
        }
    }
}
