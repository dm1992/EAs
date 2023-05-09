﻿using Bybit.Net.Clients;
using Bybit.Net.Enums;
using Bybit.Net.Objects;
using Bybit.Net.Objects.Models.Socket.Spot;
using Bybit.Net.Objects.Models.V5;
using CryptoBot.Models;
using CryptoBot.EventArgs;
using CryptoBot.Interfaces;
using CryptoBot.Interfaces.Managers;
using CryptoExchange.Net.Objects;
using CryptoExchange.Net.Sockets;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CryptoBot.Managers
{
    public class MarketManager : IMarketManager
    {
        private readonly ITradingManager _tradingManager;
        private readonly IOrderManager _orderManager;
        private readonly Config _config;
        private readonly SemaphoreSlim _tradeSemaphore;
        private readonly BybitSocketClient _webSocket;

        private List<BybitTrade> _tradeBuffer;
        private List<string> _availableSymbols;
        private Dictionary<string, BybitOrderbook> _orderBooks;
        private bool _isInitialized;

        public event EventHandler<ApplicationEventArgs> ApplicationEvent;

        public MarketManager(ITradingManager tradingManager, IOrderManager orderManager, Config config)
        {
            _tradingManager = tradingManager;
            _orderManager = orderManager;
            _config = config;
            _tradeSemaphore = new SemaphoreSlim(1, 1);

            BybitSocketClientOptions webSocketOptions = BybitSocketClientOptions.Default;
            webSocketOptions.V5StreamsOptions.OutputOriginalData = true;
            webSocketOptions.V5StreamsOptions.BaseAddress = _config.SpotStreamEndpoint;

            _webSocket = new BybitSocketClient(webSocketOptions);

            _tradeBuffer = new List<BybitTrade>();
            _orderBooks = new Dictionary<string, BybitOrderbook>();
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

        public async void InvokeWebSocketEventSubscription()
        {
            if (!_isInitialized) return;

            ApplicationEvent?.Invoke(this, new MarketManagerEventArgs(EventType.Information, "Invoking web socket event subscription."));

            CallResult<UpdateSubscription> updateSubscription = await _webSocket.V5SpotStreams.SubscribeToTradeUpdatesAsync(_availableSymbols, HandleTrades);
            updateSubscription.Data.ConnectionRestored += WebSocketEventSubscription_TradeUpdatesConnectionRestored;
            updateSubscription.Data.ConnectionLost += WebSocketEventSubscription_TradeUpdatesConnectionLost;
            updateSubscription.Data.ConnectionClosed += WebSocketEventSubscription_TradeUpdatesConnectionClosed;

            updateSubscription = await _webSocket.V5SpotStreams.SubscribeToOrderbookUpdatesAsync(_availableSymbols, 5, HandleOrderbookSnapshot, HandleOrderbookUpdate);
            updateSubscription.Data.ConnectionRestored += WebSocketEventSubscription_OrderbookUpdatesConnectionRestored;
            updateSubscription.Data.ConnectionLost += WebSocketEventSubscription_OrderbookUpdatesConnectionLost;
            updateSubscription.Data.ConnectionClosed += WebSocketEventSubscription_OrderbookUpdatesConnectionClosed;
        }

        public async void CloseWebSocketEventSubscription()
        {
            ApplicationEvent?.Invoke(this, new MarketManagerEventArgs(EventType.Information, "Closing web socket event subscription."));

            await _webSocket.V5SpotStreams.UnsubscribeAllAsync();
        }


        #region Event handlers

        private void WebSocketEventSubscription_TradeUpdatesConnectionRestored(TimeSpan obj)
        {
            ApplicationEvent?.Invoke(this, new MarketManagerEventArgs(EventType.Information, "Subscription to trade updates restored."));
        }

        private void WebSocketEventSubscription_TradeUpdatesConnectionLost()
        {
            ApplicationEvent?.Invoke(this, new MarketManagerEventArgs(EventType.Warning, "Subscription to trade updates lost."));
        }

        private void WebSocketEventSubscription_TradeUpdatesConnectionClosed()
        {
            ApplicationEvent?.Invoke(this, new MarketManagerEventArgs(EventType.Information, "Subscription to trade updates closed."));
        }

        private void WebSocketEventSubscription_OrderbookUpdatesConnectionRestored(TimeSpan obj)
        {
            ApplicationEvent?.Invoke(this, new MarketManagerEventArgs(EventType.Information, "Subscription to orderbook updates restored."));
        }

        private void WebSocketEventSubscription_OrderbookUpdatesConnectionLost()
        {
            ApplicationEvent?.Invoke(this, new MarketManagerEventArgs(EventType.Information, "Subscription to orderbook updates lost."));
        }

        private void WebSocketEventSubscription_OrderbookUpdatesConnectionClosed()
        {
            ApplicationEvent?.Invoke(this, new MarketManagerEventArgs(EventType.Information, "Subscription to orderbook updates closed."));
        }

        private void HandleTrades(DataEvent<IEnumerable<BybitTrade>> trades)
        {
            try
            {
                _tradeSemaphore.WaitAsync();

                _tradeBuffer.Concat(trades.Data);
            }
            catch (Exception e)
            {
                ApplicationEvent?.Invoke(this, new MarketManagerEventArgs(EventType.Error, $"!!!HandleTrades failed!!! {e}"));
            }
            finally
            {
                _tradeSemaphore.Release();
            }
        }

        private void HandleOrderbookSnapshot(DataEvent<BybitOrderbook> orderbook)
        {
            UpdateOrderbookEntry(orderbook.Data);
        }

        private void HandleOrderbookUpdate(DataEvent<BybitOrderbook> orderbook)
        {
            UpdateOrderbookEntry(orderbook.Data);
        }

        private void UpdateOrderbookEntry(BybitOrderbook orderbook)
        {
            if (orderbook == null) return;

            lock (_orderBooks)
            {
                if (!_orderBooks.TryGetValue(orderbook.Symbol, out _))
                {
                    _orderBooks.Add(orderbook.Symbol, orderbook);
                }
                else
                {
                    _orderBooks[orderbook.Symbol] = orderbook;
                }
            }
        }

        #endregion
    }
}
