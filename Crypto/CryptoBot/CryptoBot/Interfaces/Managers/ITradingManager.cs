using Bybit.Net.Objects.Models.V5;
using CryptoBot.Models;
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
        Task<IEnumerable<BybitAssetBalance>> GetAssetBalances();
        Task<bool> PlaceOrder(BybitOrder order);
    }
}
