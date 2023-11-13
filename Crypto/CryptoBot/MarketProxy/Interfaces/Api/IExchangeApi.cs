using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MarketClient.Interfaces.Api
{
    public interface IExchangeApi
    {
        Task<decimal?> GetPrice(string symbol);
    }
}
