using Bybit.Net.Clients;
using Bybit.Net.Enums;
using Bybit.Net.Objects;
using Bybit.Net.Objects.Models.Socket.Spot;
using CryptoBot.Data;
using CryptoBot.EventArgs;
using CryptoBot.Interfaces;
using CryptoBot.Interfaces.Managers;
using CryptoExchange.Net.Sockets;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CryptoBot.Managers.Davor
{
    public class MarketManager : IMarketManager
    {
        private readonly ITradingManager _tradingManager;
        private readonly IOrderManager _orderManager;
        private readonly Config _config;
        private readonly SemaphoreSlim _tradeSemaphore;

        private List<DataEvent<BybitSpotTradeUpdate>> _tradeBuffer;
        private List<string> _availableSymbols;
        private List<UpdateSubscription> _webSocketSubscriptions;
        private bool _isInitialized;

        public event EventHandler<ApplicationEventArgs> ApplicationEvent;

        public MarketManager(ITradingManager tradingManager, IOrderManager orderManager, Config config)
        {
            _tradingManager = tradingManager;
            _orderManager = orderManager;
            _config = config;
            _tradeSemaphore = new SemaphoreSlim(1, 1);

            _tradeBuffer = new List<DataEvent<BybitSpotTradeUpdate>>();
            _webSocketSubscriptions = new List<UpdateSubscription>();
            _isInitialized = false;
        }

        public bool Initialize()
        {
            try
            {
                if (_isInitialized) return true;

                var response = _tradingManager.GetAvailableSymbols();
                response.Wait();

                _availableSymbols = response.Result.ToList();

                if (_availableSymbols.IsNullOrEmpty())
                {
                    throw new Exception("No available symbols.");
                }

                ApplicationEvent?.Invoke(this, new MarketManagerEventArgs(EventType.Information, "Initialized."));

                _isInitialized = true;
                return true;
            }
            catch (Exception e)
            {
                ApplicationEvent?.Invoke(this, new MarketManagerEventArgs(EventType.Error, $"!!!Initialization failed!!! {e}"));
                return false;
            }
        }

        public void InvokeWebSocketEventSubscription()
        {
            if (!_isInitialized) return;

            ApplicationEvent?.Invoke(this, new MarketManagerEventArgs(EventType.Information, "Invoked web socket event subscription."));

            BybitSocketClientOptions webSocketOptions = BybitSocketClientOptions.Default;
            webSocketOptions.SpotStreamsV3Options.OutputOriginalData = true;
            webSocketOptions.SpotStreamsV3Options.BaseAddress = _config.SpotStreamEndpoint;

            BybitSocketClient webSocketClient = new BybitSocketClient(webSocketOptions);

            foreach (var symbol in _availableSymbols)
            {
                _webSocketSubscriptions.Add(webSocketClient.SpotStreamsV3.SubscribeToTradeUpdatesAsync(symbol, HandleTrade).GetAwaiter().GetResult().Data); // deadlock issue, async method in sync manner
            }

            foreach (var wss in _webSocketSubscriptions)
            {
                wss.ConnectionRestored += WebSocketSubscription_ConnectionRestored;
                wss.ConnectionLost += WebSocketSubscription_ConnectionLost;
                wss.ConnectionClosed += WebSocketSubscription_ConnectionClosed;
            }
        }

        public async void CloseWebSocketEventSubscription()
        {
            ApplicationEvent?.Invoke(this, new MarketManagerEventArgs(EventType.Information, "Closed web socket event subscription."));

            foreach (var wss in _webSocketSubscriptions)
            {
                await wss.CloseAsync();
            }
        }


        #region Event handlers

        private void WebSocketSubscription_ConnectionRestored(TimeSpan obj)
        {
            ApplicationEvent?.Invoke(this, new MarketManagerEventArgs(EventType.Information, "Web socket subscription connection restored."));
        }

        private void WebSocketSubscription_ConnectionLost()
        {
            ApplicationEvent?.Invoke(this, new MarketManagerEventArgs(EventType.Information, "Web socket subscription connection lost."));
        }

        private void WebSocketSubscription_ConnectionClosed()
        {
            ApplicationEvent?.Invoke(this, new MarketManagerEventArgs(EventType.Information, "Web socket subscription connection closed."));
        }

        private async void HandleTrade(DataEvent<BybitSpotTradeUpdate> trade)
        {
            try
            {
                await _tradeSemaphore.WaitAsync();

                trade.Topic = (string)Extensions.ParseObject(trade.OriginalData, "topic");
                if (trade.Topic == null)
                {
                    ApplicationEvent?.Invoke(this, new MarketManagerEventArgs(EventType.Error, "!!!Unable to parse topic from original trade data."));
                    return;
                }

                _tradeBuffer.Add(trade);

            }
            catch (Exception e)
            {
                ApplicationEvent?.Invoke(this, new MarketManagerEventArgs(EventType.Error, $"!!!HandleTrade failed!!! {e}"));
            }
            finally
            {
                _tradeSemaphore.Release();
            }
        }

        #endregion
    }
}
