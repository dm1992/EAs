using Bybit.Net.Clients;
using Bybit.Net.Enums;
using Bybit.Net.Objects;
using Bybit.Net.Objects.Models.V5;
using CryptoBot.Models;
using CryptoBot.EventArgs;
using CryptoBot.Interfaces.Managers;
using CryptoExchange.Net.Objects;
using CryptoExchange.Net.Sockets;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace CryptoBot.Managers
{
    public class OrderManager : IOrderManager
    {
        private readonly ITradingManager _tradingManager;
        private readonly AppConfig _config;
        private readonly SemaphoreSlim _tickerSemaphore;
        private readonly CancellationTokenSource _monitorOrderStatsCts;
        private readonly BybitSocketClient _webSocket;

        private List<string> _availableSymbols;
        private List<Order> _orderBuffer;
        private bool _isInitialized;
        private bool _dismissInvokeOrder;
        private bool _stopOrderManager;
        //private decimal _balanceProfitAmount;
        //private decimal _balanceLossAmount;
        //private decimal _orderProfitAmount;
        //private decimal _orderLossAmount;

        public event EventHandler<ApplicationEventArgs> ApplicationEvent;

        public OrderManager(ITradingManager tradingManager, AppConfig config)
        {
            _tradingManager = tradingManager;
            _config = config;
            _tickerSemaphore = new SemaphoreSlim(1, 1);
            _monitorOrderStatsCts = new CancellationTokenSource();

            BybitSocketClientOptions webSocketOptions = BybitSocketClientOptions.Default;
            webSocketOptions.V5StreamsOptions.OutputOriginalData = true;
            webSocketOptions.V5StreamsOptions.BaseAddress = _config.SpotStreamEndpoint;

            _webSocket = new BybitSocketClient(webSocketOptions);

            _orderBuffer = new List<Order>();
            _isInitialized = false;
            _dismissInvokeOrder = false;
            _stopOrderManager = false;
            //_orderProfitAmount = 0;
            //_orderLossAmount = 0;
        }

        public async void InvokeWebSocketEventSubscription()
        {
            if (!_isInitialized) return;

            ApplicationEvent?.Invoke(this, new OrderManagerEventArgs(EventType.Information, "Invoked web socket event subscription."));

            CallResult<UpdateSubscription> updateSubscription = await _webSocket.V5SpotStreams.SubscribeToTickerUpdatesAsync(_availableSymbols, HandleTicker);
            updateSubscription.Data.ConnectionRestored += WebSocketEventSubscription_TickerUpdatesConnectionRestored;
            updateSubscription.Data.ConnectionLost += WebSocketEventSubscription_TickerUpdatesConnectionLost;
            updateSubscription.Data.ConnectionClosed += WebSocketEventSubscription_TickerUpdatesConnectionClosed;
        }

        public async void CloseWebSocketEventSubscription()
        {
            ApplicationEvent?.Invoke(this, new OrderManagerEventArgs(EventType.Information, "Closed web socket event subscription."));

            await _webSocket.V5SpotStreams.UnsubscribeAllAsync();
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

                //if (!SetBalanceProfitLossAmount())
                //{
                //    throw new Exception("Failed to set balance profit/loss amount.");
                //}

                //if (!SetOrderProfitLossAmount())
                //{
                //    throw new Exception("Failed to set order profit/loss amount.");
                //}

                Task.Run(() => MonitorOrderStatsThread(_monitorOrderStatsCts.Token));

                //ApplicationEvent?.Invoke(this, new OrderManagerEventArgs(EventType.Information, 
                //$"Initialized. Balance profit amount: {_balanceProfitAmount}, balance loss amount: {_balanceLossAmount}. " +
                //$"Order profit amount: {_orderProfitAmount}, order loss amount: {_orderLossAmount}."));

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
                if (_dismissInvokeOrder) return false;

                Order order = await CreateOrder(symbol, orderSide);
                if (order == null) return false;

                return await HandleInvokeOrder(order);
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
                    await HandleFinishOrder(order);
                }

                CheckForStopOrderManager();
            }
            catch (Exception e)
            {
                ApplicationEvent?.Invoke(this, new OrderManagerEventArgs(EventType.Error, $"!!!FinishOrder failed!!! {e}"));
            }
        }

        private async Task<Order> CreateOrder(string symbol, OrderSide orderSide)
        {
            if (_orderBuffer.Where(x => x.Symbol == symbol && x.IsActive).Count() >= _config.ActiveSymbolOrders)
            {
                // wait for order(s) to finish
                return null;
            }

            Order lastOrder = _orderBuffer.Where(x => x.Symbol == symbol).OrderBy(x => x.CreateTime).LastOrDefault();
            if (lastOrder != null)
            {
                decimal? lastPrice = await _tradingManager.GetPrice(symbol);
                if (lastPrice.HasValue)
                {
                    if (orderSide == OrderSide.Buy)
                    {
                        if (lastOrder.Price >= lastPrice)
                        {
                            return null;
                        }
                    }
                    else if (orderSide == OrderSide.Sell)
                    {
                        if (lastOrder.Price <= lastPrice)
                        {
                            return null;
                        }
                    }
                }
            }

            Order order = new Order();
            order.Symbol = symbol;
            order.OrderType = OrderType.Market;
            order.Side = orderSide;
            order.Quantity = orderSide == OrderSide.Buy ? _config.BuyOrderVolume : _config.SellOrderVolume;

            if (!await _tradingManager.PlaceOrder(order))
                return null;

            return order;
        }

        private async Task<bool> HandleInvokeOrder(Order order)
        {
            if (order == null) return false;

            if (!_config.TestMode)
            {
                Order actualPlacedOrder = await _tradingManager.GetOrder(order.ClientOrderId);
                if (actualPlacedOrder == null)
                {
                    ApplicationEvent?.Invoke(this, new OrderManagerEventArgs(EventType.Error, 
                    $"!!!Unable to get order '{order.OrderId}' despite order was placed. Must delete placed order. Very strange!!!"));

                    return false;
                }

                ApplicationEvent?.Invoke(this, new OrderManagerEventArgs(EventType.Information, $"Invoked REAL order. {actualPlacedOrder.Dump()}"));

                _orderBuffer.Add(actualPlacedOrder);
            }
            else
            {
                // modify price due to incorrect test environment price
                decimal? lastPrice = await _tradingManager.GetPrice(order.Symbol);
                if (!lastPrice.HasValue)
                {
                    ApplicationEvent?.Invoke(this, new OrderManagerEventArgs(EventType.Warning, 
                    $"Failed to get last price for symbol '{order.Symbol}'. Must delete placed order. Very strange!!!"));

                    return false;
                }

                order.Price = lastPrice.Value;
                order.IsActive = true;

                ApplicationEvent?.Invoke(this, new OrderManagerEventArgs(EventType.Information, $"Invoked TEST order. {order.Dump()}"));

                _orderBuffer.Add(order);
            }

            return true;
        }

        private async Task HandleFinishOrder(Order order)
        {
            if (order == null) return;

            order.LastPrice = await _tradingManager.GetPrice(order.Symbol);

            if (!order.MustFinish)
                return;

            if (!await CloseOrder(order))
                return;

            //SetOrderRealizedProfitLossAmount(order);
            CheckForDismissInvokeOrder();

            ApplicationEvent?.Invoke(this, new OrderManagerEventArgs(EventType.Information, $"Finished order. {order.Dump()}"));
        }

        private async Task<bool> CloseOrder(Order order)
        {
            if (order == null) return false;

            if (!_config.TestMode)
            {
                // counter order used only for closing original order
                Order counterOrder = new Order();
                counterOrder.Symbol = order.Symbol;
                counterOrder.Side = order.Side == OrderSide.Buy ? OrderSide.Sell : OrderSide.Buy;
                counterOrder.Quantity = order.Quantity; //xxx check

                if (!await _tradingManager.PlaceOrder(counterOrder))
                {
                    ApplicationEvent?.Invoke(this, new OrderManagerEventArgs(EventType.Warning, 
                    $"Failed to place counter order '{counterOrder.OrderId}'. Will try to close order '{order.ClientOrderId}' later."));

                    return false;
                }

                Order actualCounterOrder = await _tradingManager.GetOrder(counterOrder.ClientOrderId);
                if (actualCounterOrder == null)
                {
                    ApplicationEvent?.Invoke(this, new OrderManagerEventArgs(EventType.Error, 
                    $"!!!Unable to get counter order '{counterOrder.OrderId}' despite counter order was placed. Very strange!!!"));
                }

                order.ExitPrice = actualCounterOrder.ExitPrice;
            }
            else
            {
                // modify price due to incorrect test environment price
                order.ExitPrice = order.LastPrice.Value;
            }

            order.UpdateTime = DateTime.Now;
            order.IsActive = false;

            return true;
        }

        //private bool ReachedBalanceProfitLossAmount(decimal balance)
        //{
        //    return balance >= _balanceProfitAmount || balance <= _balanceLossAmount;
        //}

        //private bool SetBalanceProfitLossAmount()
        //{
        //    if (_config.TestMode)
        //    {
        //        _balanceProfitAmount = _config.TestBalance * (100.0M + _config.BalanceProfitPercent) / 100.0M;
        //        _balanceLossAmount = _config.TestBalance * (100.0M -_config.BalanceLossPercent) / 100.0M;

        //        return true;
        //    }

        //    var balances = _tradingManager.GetBalances().GetAwaiter().GetResult();
        //    if (balances.IsNullOrEmpty()) 
        //        return false;

        //    decimal balance = balances.Sum(x => x.Available);
        //    _balanceProfitAmount = balance * (100.0M + _config.BalanceProfitPercent) / 100.0M;
        //    _balanceLossAmount = balance * (100.0M - _config.BalanceLossPercent) / 100.0M;

        //    return true;
        //}

        //private bool SetOrderProfitLossAmount()
        //{
        //    if (_config.TestMode)
        //    {
        //        _orderProfitAmount = _config.TestBalance * (_config.OrderProfitPercent / 100.0M);
        //        _orderLossAmount = _config.TestBalance * (_config.OrderLossPercent / 100.0M);

        //        return true;
        //    }

        //    var balances = _tradingManager.GetBalances().GetAwaiter().GetResult();
        //    if (balances.IsNullOrEmpty())
        //        return false;

        //    decimal availableBalance = balances.Sum(x => x.Available);
        //    _orderProfitAmount = availableBalance * (_config.OrderProfitPercent / 100.0M);
        //    _orderLossAmount = availableBalance * (_config.OrderLossPercent / 100.0M);

        //    return true;
        //}

        //private void SetOrderRealizedProfitLossAmount(Order order)
        //{
        //    if (order == null) return;

        //    if (order.Side == OrderSide.Buy)
        //    {
        //        order.RealizedProfitLossAmount = order.ExitPrice - order.Price; // xxx averagePrice
        //    }
        //    else if (order.Side == OrderSide.Sell)
        //    {
        //        order.RealizedProfitLossAmount = order.Price - order.ExitPrice;
        //    }

        //    if (_config.TestMode)
        //    {
        //        // we're in test mode. increase/decrease test balance
        //        _config.TestBalance += order.RealizedProfitLossAmount;
        //    }
        //}

        private void CheckForDismissInvokeOrder()
        {
            if (_dismissInvokeOrder) return;

            //if (_config.TestMode)
            //{
            //    _dismissInvokeOrder = ReachedBalanceProfitLossAmount(_config.TestBalance);
            //    return;
            //}

            var balances = _tradingManager.GetBalances().GetAwaiter().GetResult();
            if (balances.IsNullOrEmpty())
                return;

            //_dismissInvokeOrder = ReachedBalanceProfitLossAmount(balances.Sum(x => x.Available));
        }

        private void CheckForStopOrderManager()
        {
            if (_stopOrderManager) return;

            //xxx for now only this stops order manager
            if (_dismissInvokeOrder && _orderBuffer.All(x => !x.IsActive)) //xxx implement force closure of all active orders
            {
                ApplicationEvent?.Invoke(this, new OrderManagerEventArgs(EventType.TerminateApplication, "Invocation of new orders is dismissed and all opened orders completed. Terminating application."));

                CloseWebSocketEventSubscription();

                _stopOrderManager = true;
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
                            $"PROFIT orders: '{_orderBuffer.Where(x => x.Symbol == symbol && !x.IsActive && x.RealizedProfitLossAmount > 0).Count()}',\n" +
                            $"LOSS orders: '{_orderBuffer.Where(x => x.Symbol == symbol && !x.IsActive && x.RealizedProfitLossAmount < 0).Count()}'\n" +
                            $"NEUTRAL orders: '{_orderBuffer.Where(x => x.Symbol == symbol && !x.IsActive && x.RealizedProfitLossAmount == 0).Count()}'\n" +
                            $"SYMBOL BALANCE: '{_orderBuffer.Where(x => x.Symbol == symbol).Sum(x => x.RealizedProfitLossAmount)}'\n" +
                            $"------------------------\n",
                            messageScope: "orderStats"));
                        }

                        ApplicationEvent?.Invoke(this, new OrderManagerEventArgs(EventType.Information,
                        $"\n------------------------\n" +
                        $"TOTAL BALANCE: '{_orderBuffer.Sum(x => x.RealizedProfitLossAmount)}'\n" +
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

        private void WebSocketEventSubscription_TickerUpdatesConnectionRestored(TimeSpan obj)
        {
            ApplicationEvent?.Invoke(this, new OrderManagerEventArgs(EventType.Information, "Subscription to ticker updates restored."));
        }

        private void WebSocketEventSubscription_TickerUpdatesConnectionLost()
        {
            ApplicationEvent?.Invoke(this, new OrderManagerEventArgs(EventType.Warning, "Subscription to ticker updates lost."));
        }

        private void WebSocketEventSubscription_TickerUpdatesConnectionClosed()
        {
            ApplicationEvent?.Invoke(this, new OrderManagerEventArgs(EventType.Information, "Subscription to ticker updates closed."));
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
