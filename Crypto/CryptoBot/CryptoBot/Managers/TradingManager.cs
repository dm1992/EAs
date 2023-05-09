using Bybit.Net.Clients;
using Bybit.Net.Enums;
using Bybit.Net.Objects;
using Bybit.Net.Objects.Models.Spot;
using Bybit.Net.Objects.Models.Spot.v3;
using Bybit.Net.Objects.Models.V5;
using CryptoBot.Models;
using CryptoBot.EventArgs;
using CryptoBot.Interfaces.Managers;
using CryptoExchange.Net.Authentication;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace CryptoBot.Managers
{
    public class TradingManager : ITradingManager
    {
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

                ApplicationEvent?.Invoke(this, new TradingManagerEventArgs(EventType.Information, "Initialized."));

                _isInitialized = true;
                return true;
            }
            catch (Exception e)
            {
                ApplicationEvent?.Invoke(this, new TradingManagerEventArgs(EventType.Error, $"!!!Initialization failed!!! {e}"));
                return false;
            }
        }

        public async Task<bool> TradingServerAvailable()
        {
            var response = await _bybitClient.V5Api.ExchangeData.GetServerTimeAsync();
            if (!response.Success)
            {
                ApplicationEvent?.Invoke(this, new TradingManagerEventArgs(EventType.Error, 
                $"!!!Trading server unavailable. Error code: '{response.Error.Code}'. Error message: '{response.Error.Message}'!!!"));

                return false;
            }

            return true;
        }

        public async Task<IEnumerable<BybitBalance>> GetBalances()
        {
            var response = await _bybitClient.V5Api.Account.GetBalancesAsync(AccountType.Spot);                              
            if (!response.Success)
            {
                ApplicationEvent?.Invoke(this, new TradingManagerEventArgs(EventType.Error, 
                $"!!!Failed to get balances. Error code: '{response.Error.Code}'. Error message: '{response.Error.Message}'!!!"));

                return null;
            }

            return response.Data.List;
        }

        public async Task<IEnumerable<string>> GetAvailableSymbols()
        {
            if (!_config.Symbols.IsNullOrEmpty())
                return _config.Symbols.ToList();

            var response = await _bybitClient.V5Api.ExchangeData.GetSpotSymbolsAsync();
            if (!response.Success)
            {
                ApplicationEvent?.Invoke(this, new TradingManagerEventArgs(EventType.Error, 
                $"!!!Failed to get available symbols. Error code: '{response.Error.Code}'. Error message: '{response.Error.Message}'!!!"));

                return new List<string>();
            }

            return response.Data.List.Select(x => x.Name);
        }

        public async Task<decimal?> GetPrice(string symbol)
        {
            var response = await _bybitClient.V5Api.ExchangeData.GetDeliveryPriceAsync(Category.Spot, symbol);
            if (!response.Success)
            {
                ApplicationEvent?.Invoke(this, new TradingManagerEventArgs(EventType.Error, 
                $"!!!Failed to get '{symbol}' price. Error code: '{response.Error.Code}'. Error message: '{response.Error.Message}'!!!"));

                return null;
            }

            BybitDeliveryPrice priceInstance = response.Data.List.FirstOrDefault();
            if (priceInstance == null) return null;

            return priceInstance.DeliveryPrice;
        }

        public async Task<Order> GetOrder(string clientOrderId)
        {
            var response = await _bybitClient.V5Api.Trading.GetOrdersAsync(Category.Spot, null, null,null, null, clientOrderId);
            if (!response.Success)
            {
                ApplicationEvent?.Invoke(this, new TradingManagerEventArgs(EventType.Error, 
                $"!!!Failed to get order for client order id '{clientOrderId}'. Error code: '{response.Error.Code}'. Error message: '{response.Error.Message}'!!!"));

                return null;
            }

            BybitOrder activeOrder = response.Data.List.FirstOrDefault();
            if (activeOrder == null) return null;

            return (Order)activeOrder;
        }

        public async Task<bool> CancelOrder(string symbol, string clientOrderId)
        {
            var response = await _bybitClient.V5Api.Trading.CancelOrderAsync(Category.Spot, symbol, null, clientOrderId);
            if (!response.Success)
            {
                ApplicationEvent?.Invoke(this, new TradingManagerEventArgs(EventType.Error, 
                $"!!!Failed to cancel order for client order id '{clientOrderId}'. Error code: '{response.Error.Code}'. Error message: '{response.Error.Message}'!!!"));

                return false;
            }

            return true;
        }

        public async Task<bool> PlaceOrder(Order order)
        {
            if (order == null) return false;

            var response = await _bybitClient.V5Api.Trading.PlaceOrderAsync(Category.Spot, order.Symbol, order.Side, order.OrderType == OrderType.Market ? NewOrderType.Market : NewOrderType.Limit, order.Quantity, null, null, null);
            if (!response.Success)
            {
                ApplicationEvent?.Invoke(this, new TradingManagerEventArgs(EventType.Error, 
                $"!!!Failed to place order '{order.OrderId}'. Error code: '{response.Error.Code}'. Error message: '{response.Error.Message}'!!!"));

                return false;
            }

            order.OrderId = response.Data.OrderId;
            order.ClientOrderId = response.Data.ClientOrderId;

            return true;
        }
    }
}
