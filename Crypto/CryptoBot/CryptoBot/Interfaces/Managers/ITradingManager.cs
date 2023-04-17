using Bybit.Net.Objects.Models.Spot;
using Bybit.Net.Objects.Models.Spot.v3;
using CryptoBot.Interfaces.Events;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace CryptoBot.Interfaces.Managers
{
    public interface ITradingManager : IManager, IApplicationEvent
    {
        Task<bool> TradingServerAvailable();
        Task<IEnumerable<BybitSpotBalance>> GetBalances();
        Task<List<string>> GetAvailableSymbols();
        Task<decimal?> GetPrice(string symbol);
        Task<BybitSpotOrderV3> GetOrder(string clientOrderId);
        Task<bool> CancelOrder(string clientOrderId);
        Task<bool> PlaceOrder(BybitSpotOrderV3 order);
    }
}
