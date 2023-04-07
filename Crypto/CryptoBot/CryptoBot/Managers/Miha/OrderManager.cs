using Bybit.Net.Clients;
using Bybit.Net.Objects;
using Bybit.Net.Objects.Models.Socket.Spot;
using Bybit.Net.Objects.Models.Spot.v3;
using CryptoBot.Data;
using CryptoBot.EventArgs;
using CryptoBot.Interfaces;
using CryptoExchange.Net.Sockets;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CryptoBot.Managers.Miha
{
    public class OrderManager : IOrderManager
    {
        private readonly ITradingAPIManager _tradingAPIManager;
        private readonly IMarketManager _marketManager;
        private readonly Config _config;
        private readonly SemaphoreSlim _tickerSemaphore;

        private List<UpdateSubscription> _subscriptions;
        private List<BybitSpotOrderV3> _orders;
        private List<string> _availableSymbols;
        private bool _isInitialized;

        public event EventHandler<ApplicationEventArgs> ApplicationEvent;

        public OrderManager(ITradingAPIManager tradingAPIManager, IMarketManager marketManager, Config config)
        {
            _tradingAPIManager = tradingAPIManager;
            _marketManager = marketManager;
            _config = config;

            _tickerSemaphore = new SemaphoreSlim(1, 1);
            _orders = new List<BybitSpotOrderV3>();
            _subscriptions = new List<UpdateSubscription>();
            _isInitialized = false;
        }

        public bool Initialize()
        {
            try
            {
                if (_isInitialized) return true;

                var response = _tradingAPIManager.GetAvailableSymbols();
                response.Wait();

                _availableSymbols = response.Result;

                if (_availableSymbols.IsNullOrEmpty())
                {
                    ApplicationEvent?.Invoke(this, new ApplicationEventArgs(EventType.Warning,
                    message: $"Failed to initialize order manager. No available symbols."));

                    return false;
                }

                ApplicationEvent?.Invoke(this, new ApplicationEventArgs(EventType.Information,
                message: $"Initialized order manager."));

                _isInitialized = true;
                return true;
            }
            catch (Exception e)
            {
                ApplicationEvent?.Invoke(this, new ApplicationEventArgs(EventType.Error,
                message: $"!!!Initialization of order manager failed!!! {e}"));

                return false;
            }
        }

        public void InvokeAPISubscription()
        {
            if (!_isInitialized) return;

            ApplicationEvent?.Invoke(this, new ApplicationEventArgs(EventType.Information,
            $"Invoked API subscription in order manager."));

            BybitSocketClientOptions socketClientOptions = BybitSocketClientOptions.Default;
            socketClientOptions.SpotStreamsV1Options.BaseAddress = _config.SpotStreamEndpoint;

            BybitSocketClient socketClient = new BybitSocketClient(socketClientOptions);

            foreach (var symbol in _availableSymbols)
            {
                UpdateSubscription subscription = socketClient.SpotStreamsV1.SubscribeToTickerUpdatesAsync(symbol, HandleTicker).GetAwaiter().GetResult().Data;
                subscription.ConnectionRestored += API_Subscription_ConnectionRestored;
                subscription.ConnectionLost += API_Subscription_ConnectionLost;
                subscription.ConnectionClosed += API_Subscription_ConnectionClosed;

                _subscriptions.Add(subscription);
            }
        }

        public async void CloseAPISubscription()
        {
            foreach (var subscription in _subscriptions)
            {
                await subscription.CloseAsync();
            }
        }

        public async Task<bool> InvokeOrderAsync(string symbol)
        {
            if (_orders.Where(x => x.Symbol == symbol && x.IsWorking == true).Count() >= _config.ActiveSymbolOrders)
            {
                // wait for order(s) to finish
                return false;
            }

            var market = await _marketManager.GetCurrentMarket(symbol);
            if (market == null) return false;

            MarketVolumeIntensity marketVolumeIntensity = market.GetMarketVolumeIntensity();

            if (marketVolumeIntensity != MarketVolumeIntensity.Big)
            {
                ApplicationEvent?.Invoke(this, new ApplicationEventArgs(EventType.Warning,
                message: $"Not proper volume to invoke order. Current market volume intensity: {marketVolumeIntensity}."));

                return false;
            }

            MarketDirection marketDirection = market.GetMarketDirection();
            if (marketDirection == MarketDirection.Unknown)
                return false;

            BybitSpotOrderV3 placedOrder = new BybitSpotOrderV3();
            placedOrder.Symbol = symbol;
            placedOrder.Side = marketDirection == MarketDirection.Up ? OrderSide.Buy : OrderSide.Sell;
            placedOrder.Quantity = marketDirection == MarketDirection.Up ? _config.BuyOpenQuantity : _config.SellOpenQuantity;

            if (!await _tradingAPIManager.PlaceOrderAsync(placedOrder))
                return false;

            return true;
        }

        public async Task FinishOrderAsync(string symbol)
        {
            throw new NotImplementedException();
        }


        #region Event handlers

        private void API_Subscription_ConnectionRestored(TimeSpan obj)
        {
            ApplicationEvent?.Invoke(this, new ApplicationEventArgs(EventType.Information,
            message: $"API subscription connection restored in order manager."));
        }

        private void API_Subscription_ConnectionLost()
        {
            ApplicationEvent?.Invoke(this, new ApplicationEventArgs(EventType.Information,
            message: $"API subscription connection lost in order manager."));
        }

        private void API_Subscription_ConnectionClosed()
        {
            ApplicationEvent?.Invoke(this, new ApplicationEventArgs(EventType.Information,
            message: $"API subscription connection closed in order manager."));
        }

        private async void HandleTicker(DataEvent<BybitSpotTickerUpdate> ticker)
        {
            try
            {
                await _tickerSemaphore.WaitAsync();

                await FinishOrderAsync(ticker.Data.Symbol);

                await InvokeOrderAsync(ticker.Data.Symbol);
            }
            catch (Exception e)
            {
                ApplicationEvent?.Invoke(this, new ApplicationEventArgs(EventType.Error,
                message: $"!!!HandleTicker failed!!! {e}"));
            }
            finally
            {
                _tickerSemaphore.Release();
            }
        }


        #endregion
    }
}
