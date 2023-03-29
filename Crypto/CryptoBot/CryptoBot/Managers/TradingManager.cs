using Bybit.Net.Clients;
using Bybit.Net.Enums;
using Bybit.Net.Objects;
using Bybit.Net.Objects.Models.Spot;
using Bybit.Net.Objects.Models.Spot.v1;
using CryptoBot.Data;
using CryptoBot.EventArgs;
using CryptoBot.Interfaces;
using CryptoExchange.Net.Authentication;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace CryptoBot.Managers
{
    public class TradingManager : ITradingManager
    {
        private const int API_REQUEST_TIMEOUT = 50000;

        private readonly Config _config;
        private readonly BybitClient _bybitClient;

        public event EventHandler<ApplicationEventArgs> ApplicationEvent;

        public TradingManager(Config config)
        {
            _config = config;

            BybitClientOptions clientOptions = BybitClientOptions.Default;
            clientOptions.SpotApiOptions.AutoTimestamp = true;
            clientOptions.SpotApiOptions.ApiCredentials = new ApiCredentials(_config.ApiKey, _config.ApiSecret);
            clientOptions.SpotApiOptions.BaseAddress = _config.ApiEndpoint;

            _bybitClient = new BybitClient(clientOptions);

            Task.Run(() => MonitorTradingBalance());
        }

        public async Task<bool> TradingServerAvailable()
        {
            var response = await _bybitClient.SpotApiV1.ExchangeData.GetServerTimeAsync();
            if (!response.Success)
            {
                ApplicationEvent?.Invoke(this, new ApplicationEventArgs(EventType.Error,
                $"!!!Trading server unavailable. Error code: '{response.Error.Code}'. Error message: '{response.Error.Message}'!!!"));

                return false;
            }

            return true;
        }

        public async Task<IEnumerable<BybitSpotBalance>> GetBalancesAsync()
        {
            var response = await _bybitClient.SpotApiV1.Account.GetBalancesAsync(API_REQUEST_TIMEOUT);                              
            if (!response.Success)
            {
                ApplicationEvent?.Invoke(this, new ApplicationEventArgs(EventType.Error,
                $"!!!Failed to get balances. Error code: '{response.Error.Code}'. Error message: '{response.Error.Message}'!!!"));

                return null;
            }

            return response.Data;
        }

        public async Task<decimal?> GetPriceAsync(string symbol)
        {
            var response = await _bybitClient.SpotApiV1.ExchangeData.GetPriceAsync(symbol);
            if (!response.Success)
            {
                ApplicationEvent?.Invoke(this, new ApplicationEventArgs(EventType.Error,
                $"!!!Failed to get '{symbol}' price. Error code: '{response.Error.Code}'. Error message: '{response.Error.Message}'!!!"));

                return null;
            }

            return response.Data.Price;
        }

        public async Task<BybitSpotOrderV1> GetOrderAsync(string clientOrderId)
        {
            var response = await _bybitClient.SpotApiV1.Trading.GetOrderAsync(null, clientOrderId, API_REQUEST_TIMEOUT);
            if (!response.Success)
            {
                ApplicationEvent?.Invoke(this, new ApplicationEventArgs(EventType.Error,
                $"!!!Failed to get order for client order id '{clientOrderId}'. Error code: '{response.Error.Code}'. Error message: '{response.Error.Message}'!!!"));

                return null;
            }

            return response.Data;
        }

        public async Task<bool> CancelOrderAsync(string clientOrderId)
        {
            var response = await _bybitClient.SpotApiV1.Trading.CancelOrderAsync(null, clientOrderId, API_REQUEST_TIMEOUT);
            if (!response.Success)
            {
                ApplicationEvent?.Invoke(this, new ApplicationEventArgs(EventType.Error,
                $"!!!Failed to cancel order for client order id '{clientOrderId}'. Error code: '{response.Error.Code}'. Error message: '{response.Error.Message}'!!!"));

                return false;
            }

            return true;
        }

        public async Task<bool> PlaceOrderAsync(BybitSpotOrderV1 order)
        {
            if (order == null) return false;

            var response = await _bybitClient.SpotApiV1.Trading.PlaceOrderAsync(order.Symbol, order.Side, OrderType.Market, order.Quantity, null, null, null, API_REQUEST_TIMEOUT);
            if (!response.Success)
            {
                ApplicationEvent?.Invoke(this, new ApplicationEventArgs(EventType.Error,
                $"!!!Failed to place order '{order.Id}'. Error code: '{response.Error.Code}'. Error message: '{response.Error.Message}'!!!"));

                return false;
            }

            order.Id = response.Data.Id;
            order.ClientOrderId = response.Data.ClientOrderId;

            return true;
        }

        private async Task MonitorTradingBalance()
        {
            while (true)
            {
                try
                {
                    IEnumerable<BybitSpotBalance> balance = await GetBalancesAsync();
                    if (balance.IsNullOrEmpty())
                    {
                        ApplicationEvent?.Invoke(this, new ApplicationEventArgs(EventType.Error,
                        "!!!Failed to obtain trading balance!!!"));
                    }
                    else
                    {
                        ApplicationEvent?.Invoke(this, new ApplicationEventArgs(EventType.Information,
                        $"Trading balance:\n{String.Join("\n", balance.Select(x => $"Asset: '{x.Asset}', Available: '{x.Available}', Locked: '{x.Locked}', Total: '{x.Total}'"))}"));
                    }
                }
                catch (Exception e)
                {
                    ApplicationEvent?.Invoke(this, new ApplicationEventArgs(EventType.Error,
                    $"!!!MonitorTradingBalance failed!!! {e}"));
                }

                Task.Delay(30000).Wait();
            }
        }
    }
}
