using CryptoBot.Models;
using CryptoBot.Interfaces.Events;
using System.Collections.Generic;
using System.Threading.Tasks;
using Bybit.Net.Objects.Models.V5;

namespace CryptoBot.Interfaces.Managers
{
    public interface ITradingManager : IManager, IApplicationEvent
    {
        Task<bool> TradingServerAvailable();
        Task<IEnumerable<BybitBalance>> GetBalances();
        Task<IEnumerable<string>> GetAvailableSymbols();
        Task<decimal?> GetPrice(string symbol);
        //Task<Order> GetOrder(string clientOrderId);
        //Task<bool> CancelOrder(string symbol, string clientOrderId);
        //Task<bool> PlaceOrder(Order order);
    }
}
