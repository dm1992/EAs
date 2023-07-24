using Bybit.Net.Objects.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CryptoBot.Interfaces.Managers
{
    public interface ITradingManager : IManager
    {
        Task<bool> TradingServerAvailable();
        Task<IDictionary<string, BybitBalance>> GetBalances();
        Task<bool> PlaceOrder(BybitUsdPerpetualOrder order);
        Task<bool> RemoveOrder(string symbol, string orderId);
        Task<BybitUsdPerpetualOrder> GetOrder(string symbol, string orderId);
    }
}
