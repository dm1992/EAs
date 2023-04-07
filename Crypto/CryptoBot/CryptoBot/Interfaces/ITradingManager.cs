﻿using Bybit.Net.Objects.Models.Spot;
using Bybit.Net.Objects.Models.Spot.v1;
using CryptoExchange.Net.CommonObjects;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace CryptoBot.Interfaces
{
    public interface ITradingAPIManager : IApplicationEvent
    {
        Task<bool> TradingServerAvailable();
        Task<IEnumerable<BybitSpotBalance>> GetBalancesAsync();
        Task<List<string>> GetAvailableSymbols();
        Task<decimal?> GetPriceAsync(string symbol);
        Task<BybitSpotOrderV1> GetOrderAsync(string clientOrderId);
        Task<bool> CancelOrderAsync(string clientOrderId);
        Task<bool> PlaceOrderAsync(BybitSpotOrderV1 order);
    }
}
