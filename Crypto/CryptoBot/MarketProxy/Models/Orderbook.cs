using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MarketProxy.Models
{
    public class Orderbook
    {
        public string Symbol { get; set; }
        public List<Bid> Bids { get; set; }
        public List<Ask> Asks { get; set; }

        public Orderbook()
        {
            this.Bids = new List<Bid>();
            this.Asks = new List<Ask>();
        }
    }

    public class Bid
    {
        public decimal Price { get; set; }
        public decimal Volume { get; set; }

        public Bid(decimal price, decimal volume)
        {
            this.Price = price;
            this.Volume = volume;
        }

        public string Dump()
        {
            return $"{this.Volume};{this.Price}";
        }

    }

    public class Ask
    {
        public decimal Price { get; set; }
        public decimal Volume { get; set; }

        public Ask(decimal price, decimal volume)
        {
            this.Price = price;
            this.Volume = volume;
        }

        public string Dump()
        {
            return $"{this.Volume};{this.Price}";
        }
    }
}
