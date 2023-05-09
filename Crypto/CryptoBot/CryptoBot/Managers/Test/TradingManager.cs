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

namespace CryptoBot.Managers.Test
{
    public class TradingManager : ITradingManager
    {
        private const int DELAY = 500;

        private readonly Config _config;
        private readonly BybitClient _bybitClient;

        private string _currentOrderId = null;
        private string _currentClientOrderId = null;

        public event EventHandler<ApplicationEventArgs> ApplicationEvent;

        public TradingManager(Config config)
        {
            _config = config;

            BybitClientOptions clientOptions = BybitClientOptions.Default;
            clientOptions.SpotApiOptions.AutoTimestamp = true;
            clientOptions.SpotApiOptions.BaseAddress = "https://api.bybit.com";

            _bybitClient = new BybitClient(clientOptions);
        }

        public bool Initialize()
        {
            ApplicationEvent?.Invoke(this, new TradingManagerEventArgs(EventType.Information, $"Initialized."));
            return true;
        }

        public async Task<bool> TradingServerAvailable()
        {
            var response = await _bybitClient.SpotApiV3.ExchangeData.GetServerTimeAsync();
            if (!response.Success)
            {
                ApplicationEvent?.Invoke(this, new TradingManagerEventArgs(EventType.Error, $"!!!Trading server unavailable. Error code: '{response.Error.Code}'. Error message: '{response.Error.Message}'!!!"));
                return false;
            }

            return true;
        }

        public async Task<IEnumerable<BybitSpotBalance>> GetBalances()
        {
            await Task.Delay(DELAY);

            BybitSpotBalance balance1 = new BybitSpotBalance();
            balance1.Asset = "BTC";
            balance1.Available = 0.45M;
            balance1.Locked = 0;

            BybitSpotBalance balance2 = new BybitSpotBalance();
            balance2.Asset = "USDT";
            balance2.Available = 2100M;
            balance2.Locked = 0;

            return new List<BybitSpotBalance>() { balance1, balance2 };
        }

        public async Task<IEnumerable<string>> GetAvailableSymbols()
        {
            if (!_config.Symbols.IsNullOrEmpty())
                return _config.Symbols.ToList();

            var response = await _bybitClient.SpotApiV3.ExchangeData.GetSymbolsAsync();
            if (!response.Success)
            {
                ApplicationEvent?.Invoke(this, new TradingManagerEventArgs(EventType.Error, $"!!!Failed to get available symbols. Error code: '{response.Error.Code}'. Error message: '{response.Error.Message}'!!!"));
                return new List<string>();
            }

            return response.Data.Select(x => x.Name);
        }

        public async Task<decimal?> GetPrice(string symbol)
        {
            var response = await _bybitClient.SpotApiV3.ExchangeData.GetPriceAsync(symbol);
            if (!response.Success)
            {
                ApplicationEvent?.Invoke(this, new TradingManagerEventArgs(EventType.Error, $"!!!Failed to get '{symbol}' price. Error code: '{response.Error.Code}'. Error message: '{response.Error.Message}'!!!"));
                return null;
            }

            return response.Data.Price;
        }

        public async Task<Order> GetOrder(string clientOrderId)
        {
            await Task.Delay(DELAY);

            Order order = new Order();
            order.OrderId = _currentOrderId;
            order.ClientOrderId = _currentClientOrderId;
            order.IsActive = true;

            return order;
        }

        public async Task<bool> CancelOrder(string clientOrderId)
        {
            await Task.Delay(DELAY);
            return true;
        }

        public async Task<bool> PlaceOrder(Order order)
        {
            order.OrderId = "test";
            order.ClientOrderId = Guid.NewGuid().ToString();

            _currentOrderId = order.OrderId;
            _currentClientOrderId = order.ClientOrderId;

            await Task.Delay(DELAY);
            return true;
        }
    }
}
