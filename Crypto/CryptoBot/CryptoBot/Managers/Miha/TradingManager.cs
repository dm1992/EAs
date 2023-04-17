using Bybit.Net.Clients;
using Bybit.Net.Objects;
using Bybit.Net.Objects.Models.Spot;
using Bybit.Net.Objects.Models.Spot.v3;
using CryptoBot.Data;
using CryptoBot.EventArgs;
using CryptoBot.Interfaces.Managers;
using CryptoExchange.Net.Authentication;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace CryptoBot.Managers.Miha
{
    public class TradingManager : ITradingManager
    {
        private const int API_REQUEST_TIMEOUT = 50000;

        private readonly Config _config;
        private readonly BybitClient _bybitClient;

        private bool _isInitialized;

        public event EventHandler<ApplicationEventArgs> ApplicationEvent;

        public TradingManager(Config config)
        {
            _config = config;

            BybitClientOptions clientOptions = BybitClientOptions.Default;
            clientOptions.SpotApiOptions.AutoTimestamp = true;
            clientOptions.SpotApiOptions.ApiCredentials = new ApiCredentials(_config.ApiKey, _config.ApiSecret);
            clientOptions.SpotApiOptions.BaseAddress = _config.ApiEndpoint;

            _bybitClient = new BybitClient(clientOptions);
            _isInitialized = false;
        }

        public bool Initialize()
        {
            try
            {
                if (_isInitialized) return true;

                Task.Run(() => MonitorTradingBalance());

                ApplicationEvent?.Invoke(this, new ApplicationEventArgs(EventType.Information,
                message: $"Initialized trading manager."));

                _isInitialized = true;
                return true;
            }
            catch (Exception e)
            {
                ApplicationEvent?.Invoke(this, new ApplicationEventArgs(EventType.Error,
                message: $"!!!Initialization of trading manager failed!!! {e}"));

                return false;
            }
        }

        public async Task<bool> TradingServerAvailable()
        {
            var response = await _bybitClient.SpotApiV3.ExchangeData.GetServerTimeAsync();
            if (!response.Success)
            {
                ApplicationEvent?.Invoke(this, new ApplicationEventArgs(EventType.Error,
                $"!!!Trading server unavailable. Error code: '{response.Error.Code}'. Error message: '{response.Error.Message}'!!!"));

                return false;
            }

            return true;
        }

        public async Task<IEnumerable<BybitSpotBalance>> GetBalances()
        {
            var response = await _bybitClient.SpotApiV3.Account.GetBalancesAsync(API_REQUEST_TIMEOUT);                              
            if (!response.Success)
            {
                ApplicationEvent?.Invoke(this, new ApplicationEventArgs(EventType.Error,
                $"!!!Failed to get balances. Error code: '{response.Error.Code}'. Error message: '{response.Error.Message}'!!!"));

                return null;
            }

            return response.Data;
        }

        public async Task<List<string>> GetAvailableSymbols()
        {
            if (!_config.Symbols.IsNullOrEmpty())
                return _config.Symbols.ToList();

            var response = await _bybitClient.SpotApiV3.ExchangeData.GetSymbolsAsync();
            if (!response.Success)
            {
                ApplicationEvent?.Invoke(this, new ApplicationEventArgs(EventType.Error,
                message: $"!!!Failed to get available symbols. Error code: '{response.Error.Code}'. Error message: '{response.Error.Message}'!!!"));

                return new List<string>();
            }

            return response.Data.Select(x => x.Name).ToList();
        }

        public async Task<decimal?> GetPrice(string symbol)
        {
            var response = await _bybitClient.SpotApiV3.ExchangeData.GetPriceAsync(symbol);
            if (!response.Success)
            {
                ApplicationEvent?.Invoke(this, new ApplicationEventArgs(EventType.Error,
                $"!!!Failed to get '{symbol}' price. Error code: '{response.Error.Code}'. Error message: '{response.Error.Message}'!!!"));

                return null;
            }

            return response.Data.Price;
        }

        public async Task<BybitSpotOrderV3> GetOrder(string clientOrderId)
        {
            var response = await _bybitClient.SpotApiV3.Trading.GetOrderAsync(null, clientOrderId, API_REQUEST_TIMEOUT);
            if (!response.Success)
            {
                ApplicationEvent?.Invoke(this, new ApplicationEventArgs(EventType.Error,
                $"!!!Failed to get order for client order id '{clientOrderId}'. Error code: '{response.Error.Code}'. Error message: '{response.Error.Message}'!!!"));

                return null;
            }

            return response.Data;
        }

        public async Task<bool> CancelOrder(string clientOrderId)
        {
            var response = await _bybitClient.SpotApiV3.Trading.CancelOrderAsync(null, clientOrderId, API_REQUEST_TIMEOUT);
            if (!response.Success)
            {
                ApplicationEvent?.Invoke(this, new ApplicationEventArgs(EventType.Error,
                $"!!!Failed to cancel order for client order id '{clientOrderId}'. Error code: '{response.Error.Code}'. Error message: '{response.Error.Message}'!!!"));

                return false;
            }

            return true;
        }

        public async Task<bool> PlaceOrder(BybitSpotOrderV3 order)
        {
            if (order == null) return false;

            var response = await _bybitClient.SpotApiV3.Trading.PlaceOrderAsync(order.Symbol, order.Side, order.Type, order.Quantity, null, null, null, API_REQUEST_TIMEOUT);
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
                    IEnumerable<BybitSpotBalance> balance = await GetBalances();
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
