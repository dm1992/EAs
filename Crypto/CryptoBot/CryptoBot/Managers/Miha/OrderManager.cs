using Bybit.Net.Clients;
using Bybit.Net.Objects;
using Bybit.Net.Objects.Models.Socket.Spot;
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
        private readonly ITradingManager _tradingManager;
        private readonly IMarketManager _marketManager;
        private readonly Config _config;
        private readonly SemaphoreSlim _tickerSemaphore;

        private List<UpdateSubscription> _subscriptions;
        private bool _isInitialized;

        public event EventHandler<ApplicationEventArgs> ApplicationEvent;

        public OrderManager(ITradingManager tradingManager, IMarketManager marketManager, Config config)
        {
            _tradingManager = tradingManager;
            _marketManager = marketManager;
            _config = config;

            _tickerSemaphore = new SemaphoreSlim(1, 1);
            _subscriptions = new List<UpdateSubscription>();
            _isInitialized = false;
        }

        public bool Initialize()
        {
            try
            {
                if (_isInitialized) return true;


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
        }

        public async void CloseAPISubscription()
        {
            foreach (var subscription in _subscriptions)
            {
                await subscription.CloseAsync();
            }
        }

        public Task<bool> InvokeOrderAsync(string symbol)
        {
            throw new NotImplementedException();
        }

        public Task FinishOrderAsync(string symbol)
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
