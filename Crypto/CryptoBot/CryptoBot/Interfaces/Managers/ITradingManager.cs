using Bybit.Net.Objects.Models.Spot;
using CryptoBot.Data;
using CryptoBot.Interfaces.Events;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace CryptoBot.Interfaces.Managers
{
    public interface ITradingManager : IManager, IApplicationEvent
    {
        Task<bool> TradingServerAvailable();
        Task<IEnumerable<BybitSpotBalance>> GetBalances();
        Task<IEnumerable<string>> GetAvailableSymbols();
        Task<decimal?> GetPrice(string symbol);
        Task<Order> GetOrder(string clientOrderId);
        Task<bool> CancelOrder(string clientOrderId);
        Task<bool> PlaceOrder(Order order);
    }
}
