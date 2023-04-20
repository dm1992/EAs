using Bybit.Net.Clients;
using Bybit.Net.Enums;
using Bybit.Net.Objects;
using Bybit.Net.Objects.Models.Socket.Spot;
using Bybit.Net.Objects.Models.Spot.v3;
using CryptoBot.Data;
using CryptoBot.EventArgs;
using CryptoBot.Interfaces.Managers;
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
        private readonly Config _config;
        private readonly SemaphoreSlim _tickerSemaphore;
        private readonly CancellationTokenSource _monitorOrderStatsCts;

        private List<string> _availableSymbols;
        private List<UpdateSubscription> _webSocketSubscriptions;
        private List<Order> _orderBuffer;
        private bool _isInitialized;
        private decimal _roiOrderAmount;
        private decimal _riskOrderAmount;

        public event EventHandler<ApplicationEventArgs> ApplicationEvent;

        public OrderManager(ITradingManager tradingManager, Config config)
        {
            _tradingManager = tradingManager;
            _config = config;
            _tickerSemaphore = new SemaphoreSlim(1, 1);
            _monitorOrderStatsCts = new CancellationTokenSource();

            _orderBuffer = new List<Order>();
            _webSocketSubscriptions = new List<UpdateSubscription>();
            _isInitialized = false;
            _roiOrderAmount = 0;
            _riskOrderAmount = 0;
        }

        public void InvokeWebSocketEventSubscription()
        {
            if (!_isInitialized) return;

            ApplicationEvent?.Invoke(this, new OrderManagerEventArgs(EventType.Information, "Invoked web socket event subscription."));

            BybitSocketClientOptions webSocketOptions = BybitSocketClientOptions.Default;
            webSocketOptions.SpotStreamsV3Options.OutputOriginalData = true;
            webSocketOptions.SpotStreamsV3Options.BaseAddress = _config.SpotStreamEndpoint;

            BybitSocketClient webSocketClient = new BybitSocketClient(webSocketOptions);

            foreach (var symbol in _availableSymbols)
            {
                _webSocketSubscriptions.Add(webSocketClient.SpotStreamsV3.SubscribeToTickerUpdatesAsync(symbol, HandleTicker).GetAwaiter().GetResult().Data); // deadlock issue, async method in sync manner
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
            ApplicationEvent?.Invoke(this, new OrderManagerEventArgs(EventType.Information, "Closed web socket event subscription."));

            foreach (var wss in _webSocketSubscriptions)
            {
                await wss.CloseAsync();
            }
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

                if (!CalculateOrderPLFactor())
                {
                    throw new Exception("Not able to calculate PL factor.");
                }

                Task.Run(() => MonitorOrderStatsThread(_monitorOrderStatsCts.Token));

                ApplicationEvent?.Invoke(this, new OrderManagerEventArgs(EventType.Information, $"Initialized. PL order factors - ROI order amount: {_roiOrderAmount}, risk order amount: {_riskOrderAmount}."));

                _isInitialized = true;
                return true;
            }
            catch (Exception e)
            {
                ApplicationEvent?.Invoke(this, new OrderManagerEventArgs(EventType.Error, $"!!!Initialization failed!!! {e}"));
                return false;
            }
        }

        public async Task<bool> InvokeOrder(string symbol, OrderSide orderSide)
        {
            try
            {
                if (_orderBuffer.Where(x => x.Symbol == symbol && x.IsActive).Count() >= _config.ActiveSymbolOrders)
                {
                    // wait for order(s) to finish
                    return false;
                }

                Order placedOrder = new Order();
                placedOrder.Symbol = symbol;
                placedOrder.Type = OrderType.Market;
                placedOrder.Side = orderSide;
                placedOrder.Quantity = orderSide == OrderSide.Buy ? _config.BuyOrderVolume : _config.SellOrderVolume;

                if (!await _tradingManager.PlaceOrder(placedOrder))
                    return false;

                if (!_config.TestMode)
                {
                    Order order = await _tradingManager.GetOrder(placedOrder.ClientOrderId);
                    if (order == null)
                    {
                        ApplicationEvent?.Invoke(this, new OrderManagerEventArgs(EventType.Error, $"!!!Unable to get order '{order.Id}' despite order was placed. Must delete placed order. Very strange!!!"));
                        return false;
                    }

                    if (!SetOrderPLPrice(order))
                    {
                        ApplicationEvent?.Invoke(this, new OrderManagerEventArgs(EventType.Error, $"!!!Unable to set order '{order.Id}' PL price. Must delete placed order."));
                        return false;
                    }

                    _orderBuffer.Add(order);

                    ApplicationEvent?.Invoke(this, new OrderManagerEventArgs(EventType.Information, $"Invoked REAL order. {order.Dump()}"));
                }
                else
                {
                    // modify price due to incorrect test environment price
                    decimal? lastPrice = await _tradingManager.GetPrice(symbol);
                    if (!lastPrice.HasValue)
                    {
                        ApplicationEvent?.Invoke(this, new OrderManagerEventArgs(EventType.Warning, $"Failed to get last price for symbol '{symbol}'. Must delete placed order. Very strange!!!"));
                        return false;
                    }

                    placedOrder.Price = lastPrice.Value;
                    placedOrder.IsActive = true;

                    if (!SetOrderPLPrice(placedOrder))
                    {
                        ApplicationEvent?.Invoke(this, new OrderManagerEventArgs(EventType.Error, $"!!!Unable to set order '{placedOrder.Id}' PL price. Must delete placed order."));
                        return false;
                    }

                    _orderBuffer.Add(placedOrder);

                    ApplicationEvent?.Invoke(this, new OrderManagerEventArgs(EventType.Information, $"Invoked TEST order. {placedOrder.Dump()}"));
                }

                return true;
            }
            catch (Exception e)
            {
                ApplicationEvent?.Invoke(this, new OrderManagerEventArgs(EventType.Error, $"!!!InvokeOrder failed!!! {e}"));
                return false;
            }
        }

        public async Task FinishOrder(string symbol)
        {
            try
            {
                foreach (Order order in _orderBuffer.Where(x => x.Symbol == symbol && x.IsActive))
                {
                    decimal? lastPrice = await _tradingManager.GetPrice(symbol);
                    if (!lastPrice.HasValue || lastPrice.Value == order.Price)
                    {
                        // nothing to do
                        continue;
                    }

                    bool shouldFinish = false;

                    if (order.Side == OrderSide.Buy)
                    {
                        if (lastPrice >= order.TakeProfitPrice)
                        {
                            shouldFinish = true;
                        }
                        else if (lastPrice <= order.StopLossPrice)
                        {
                            shouldFinish = true;
                        }
                    }
                    else if (order.Side == OrderSide.Sell)
                    {
                        if (lastPrice <= order.TakeProfitPrice)
                        {
                            shouldFinish = true;
                        }
                        else if (lastPrice >= order.StopLossPrice)
                        {
                            shouldFinish = true;
                        }
                    }

                    if (shouldFinish)
                    {
                        if (!_config.TestMode)
                        {
                            // counter order used only for closing original order
                            Order placedCounterOrder = new Order();
                            placedCounterOrder.Symbol = order.Symbol;
                            placedCounterOrder.Side = order.Side == OrderSide.Buy ? OrderSide.Sell : OrderSide.Buy;
                            placedCounterOrder.Quantity = order.Side == OrderSide.Buy ? order.QuantityFilled : order.QuoteQuantity;

                            if (!await _tradingManager.PlaceOrder(placedCounterOrder))
                            {
                                ApplicationEvent?.Invoke(this, new OrderManagerEventArgs(EventType.Warning, $"Failed to place counter order '{placedCounterOrder.Id}'. Will try to finish order '{order.Id}' later."));
                                continue;
                            }

                            Order counterOrder = await _tradingManager.GetOrder(placedCounterOrder.ClientOrderId);
                            if (counterOrder == null)
                            {
                                ApplicationEvent?.Invoke(this, new OrderManagerEventArgs(EventType.Error, $"!!!Unable to get counter order '{placedCounterOrder.Id}' despite counter order was placed. Very strange!!!"));
                            }

                            order.ExitPrice = counterOrder.AveragePrice;
                        }
                        else
                        {
                            // modify price due to incorrect test environment price
                            order.ExitPrice = lastPrice.Value;
                        }

                        order.UpdateTime = DateTime.Now;
                        order.IsActive = false;

                        HandleFinishedOrder(order);

                        //xxx is needed balance reached? Close API subscription connection.
                        ApplicationEvent?.Invoke(this, new OrderManagerEventArgs(EventType.Information, $"Finished invoked order. {order.Dump()}"));
                    }
                }
            }
            catch (Exception e)
            {
                ApplicationEvent?.Invoke(this, new OrderManagerEventArgs(EventType.Error, $"!!!FinishOrder failed!!! {e}"));
            }
        }

        private bool SetOrderPLPrice(Order order)
        {
            if (order == null) return false;

            if (order.Side == OrderSide.Buy)
            {
                order.TakeProfitPrice = order.Price + _roiOrderAmount;
                order.StopLossPrice = order.Price - _riskOrderAmount;

                return true;
            }
            
            if (order.Side == OrderSide.Sell)
            {
                order.TakeProfitPrice = order.Price - _roiOrderAmount;
                order.StopLossPrice = order.Price + _riskOrderAmount;

                return true;
            }

            return false;
        }

        private bool CalculateOrderPLFactor()
        {
            if (_config.TestMode)
            {
                _roiOrderAmount = _config.TestAvailableBalance * (_config.ROIPercentage / 100.0M);
                _riskOrderAmount = _config.TestAvailableBalance * (_config.RiskPercentage / 100.0M);

                return true;
            }

            var balances = _tradingManager.GetBalances().GetAwaiter().GetResult();
            if (balances.IsNullOrEmpty())
                return false;

            decimal availableBalance = balances.Sum(x => x.Available);
            _roiOrderAmount = availableBalance * (_config.ROIPercentage / 100.0M);
            _riskOrderAmount = availableBalance * (_config.RiskPercentage / 100.0M);

            return true;
        }

        private void HandleFinishedOrder(Order order)
        {
            if (order == null) return;

            if (order.Side == OrderSide.Buy)
            {
                order.RealizedPL = order.ExitPrice - order.Price; // xxx averagePrice
            }
            else if (order.Side == OrderSide.Sell)
            {
                order.RealizedPL = order.Price - order.ExitPrice;
            }
        }

        private void MonitorOrderStatsThread(CancellationToken cancellationToken)
        {
            while (true)
            {
                try
                {
                    if (cancellationToken.IsCancellationRequested)
                        break;

                    lock (_orderBuffer)
                    {
                        foreach (var symbol in _config.Symbols)
                        {
                            ApplicationEvent?.Invoke(this, new OrderManagerEventArgs(EventType.Information,
                            $"\n------------------------\n" +
                            $"SYMBOL: '{symbol}'\n" +
                            $"------------------------\n" +
                            $"ACTIVE orders: '{_orderBuffer.Where(x => x.Symbol == symbol && x.IsActive).Count()}',\n" +
                            $"PROFIT orders: '{_orderBuffer.Where(x => x.Symbol == symbol && !x.IsActive && x.RealizedPL > 0).Count()}',\n" +
                            $"LOSS orders: '{_orderBuffer.Where(x => x.Symbol == symbol && !x.IsActive && x.RealizedPL < 0).Count()}'\n" +
                            $"NEUTRAL orders: '{_orderBuffer.Where(x => x.Symbol == symbol && !x.IsActive && x.RealizedPL == 0).Count()}'\n" +
                            $"SYMBOL BALANCE: '{_orderBuffer.Where(x => x.Symbol == symbol).Sum(x => x.RealizedPL)}'\n" +
                            $"------------------------\n",
                            messageScope: "orderStats"));
                        }

                        ApplicationEvent?.Invoke(this, new OrderManagerEventArgs(EventType.Information,
                        $"\n------------------------\n" +
                        $"TOTAL BALANCE: '{_orderBuffer.Sum(x => x.RealizedPL)}'\n" +
                        $"------------------------\n",
                        messageScope: "orderStats"));
                    }
                }
                catch (Exception e)
                {
                    ApplicationEvent?.Invoke(this, new OrderManagerEventArgs(EventType.Error, $"!!!MonitorOrderStatsThread failed!!! {e}"));
                }

                Task.Delay(30000).Wait();
            }
        }


        #region Event handlers

        private void WebSocketSubscription_ConnectionRestored(TimeSpan obj)
        {
            ApplicationEvent?.Invoke(this, new OrderManagerEventArgs(EventType.Information, "Web socket subscription connection restored."));
        }

        private void WebSocketSubscription_ConnectionLost()
        {
            ApplicationEvent?.Invoke(this, new OrderManagerEventArgs(EventType.Information, "Web socket subscription connection lost."));
        }

        private void WebSocketSubscription_ConnectionClosed()
        {
            ApplicationEvent?.Invoke(this, new OrderManagerEventArgs(EventType.Information, "Web socket subscription connection closed."));
        }

        private async void HandleTicker(DataEvent<BybitSpotTickerUpdate> ticker)
        {
            try
            {
                await _tickerSemaphore.WaitAsync();

                await FinishOrder(ticker.Data.Symbol);
            }
            catch (Exception e)
            {
                ApplicationEvent?.Invoke(this, new OrderManagerEventArgs(EventType.Error, $"!!!HandleTicker failed!!! {e}"));
            }
            finally
            {
                _tickerSemaphore.Release();
            }
        }

        #endregion
    }
}
