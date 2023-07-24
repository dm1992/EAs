using Bybit.Net.Objects.Models.Spot;
using Bybit.Net.Objects.Models.Spot.v3;
using Bybit.Net.Objects.Models.V5;
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
        Task<IEnumerable<BybitSpotBalance>> GetBalances();
        Task<bool> PlaceOrder(BybitSpotOrderV3 order);
        Task<BybitSpotOrderV3> GetOrder(string orderId);
    }
}
