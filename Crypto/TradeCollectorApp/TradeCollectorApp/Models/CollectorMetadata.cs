using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TradeCollectorApp.Models
{
    public class CollectorMetadata
    {
        public CollectorMetadata()
        {
            TradeBuffer = new List<Trade>();
        }

        public string Symbol { get; set; }
        public int CollectionTimeout { get; set; }
        public DateTime? CollectionStartedAt { get; set; }
        public List<Trade> TradeBuffer { get; set; }
        public bool CollectionFinished 
        {
            get
            {
                return this.CollectionStartedAt.HasValue && (DateTime.Now - this.CollectionStartedAt.Value).TotalMilliseconds >= this.CollectionTimeout;
            }
        }
    }
}
