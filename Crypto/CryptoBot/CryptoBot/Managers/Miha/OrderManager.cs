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
        private bool _dismissInvokeOrder;
        private bool _stopOrderManager;
        private decimal _balanceProfitAmount;
        private decimal _balanceLossAmount;
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
            _dismissInvokeOrder = false;
            _stopOrderManager = false;
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

                if (!SetBalanceProfitLossAmount())
                {
                    throw new Exception("Failed to set balance profit/loss amount.");
                }

                if (!SetOrderProfitLossAmount())
                {
                    throw new Exception("Failed to set order profit/loss amount.");
                }

                Task.Run(() => MonitorOrderStatsThread(_monitorOrderStatsCts.Token));

                ApplicationEvent?.Invoke(this, new OrderManagerEventArgs(EventType.Information, 
                $"Initialized. Balance profit amount: {_balanceProfitAmount}, balance loss amount: {_balanceLossAmount}. " +
                $"Order ROI amount: {_roiOrderAmount}, order risk amount: {_riskOrderAmount}."));

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

                return await HandleOrder(order);
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
                    order.LastPrice = await _tradingManager.GetPrice(symbol);

                    if (!order.MustFinish) 
                        continue;

                    if (!await CloseOrder(order))
                        continue;

                    SetOrderRealizedProfitLossAmount(order);

                    ApplicationEvent?.Invoke(this, new OrderManagerEventArgs(EventType.Information, $"Finished order. {order.Dump()}"));

                    DismissInvokeOrder();
                }

                StopOrderManager();
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

            Order order = new Order();
            order.Symbol = symbol;
            order.Type = OrderType.Market;
            order.Side = orderSide;
            order.Quantity = orderSide == OrderSide.Buy ? _config.BuyOrderVolume : _config.SellOrderVolume;

            if (!await _tradingManager.PlaceOrder(order))
            {
                // failed to place order
                order = null;
            }

            return order;
        }

        private async Task<bool> HandleOrder(Order order)
        {
            if (order == null) return false;

            if (!_config.TestMode)
            {
                Order latestOrder = await _tradingManager.GetOrder(order.ClientOrderId);
                if (latestOrder == null)
                {
                    ApplicationEvent?.Invoke(this, new OrderManagerEventArgs(EventType.Error, $"!!!Unable to get order '{order.Id}' despite order was placed. Must delete placed order. Very strange!!!"));
                    return false;
                }

                SetOrderTakeProfitAndStopLossPrice(latestOrder);

                _orderBuffer.Add(latestOrder);

                ApplicationEvent?.Invoke(this, new OrderManagerEventArgs(EventType.Information, $"Invoked REAL order. {latestOrder.Dump()}"));
            }
            else
            {
                // modify price due to incorrect test environment price
                decimal? lastPrice = await _tradingManager.GetPrice(order.Symbol);
                if (!lastPrice.HasValue)
                {
                    ApplicationEvent?.Invoke(this, new OrderManagerEventArgs(EventType.Warning, $"Failed to get last price for symbol '{order.Symbol}'. Must delete placed order. Very strange!!!"));
                    return false;
                }

                order.Price = lastPrice.Value;
                order.IsActive = true;

                SetOrderTakeProfitAndStopLossPrice(order);

                _orderBuffer.Add(order);

                ApplicationEvent?.Invoke(this, new OrderManagerEventArgs(EventType.Information, $"Invoked TEST order. {order.Dump()}"));
            }

            return true;
        }

        private async Task<bool> CloseOrder(Order order)
        {
            if (order == null) return false;

            if (!_config.TestMode)
            {
                // counter order used only for closing original order
                Order placedCounterOrder = new Order();
                placedCounterOrder.Symbol = order.Symbol;
                placedCounterOrder.Side = order.Side == OrderSide.Buy ? OrderSide.Sell : OrderSide.Buy;
                placedCounterOrder.Quantity = order.Side == OrderSide.Buy ? order.QuantityFilled : order.QuoteQuantity;

                if (!await _tradingManager.PlaceOrder(placedCounterOrder))
                {
                    ApplicationEvent?.Invoke(this, new OrderManagerEventArgs(EventType.Warning, $"Failed to place counter order '{placedCounterOrder.Id}'. Will try to close order '{order.Id}' later."));
                    return false;
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
                order.ExitPrice = order.LastPrice.Value;
            }

            order.UpdateTime = DateTime.Now;
            order.IsActive = false;

            return true;
        }

        private bool ReachedBalanceProfitLossAmount(decimal balance)
        {
            return balance >= _balanceProfitAmount || balance <= _balanceLossAmount;
        }

        private bool SetBalanceProfitLossAmount()
        {
            if (_config.TestMode)
            {
                _balanceProfitAmount = _config.TestBalance * (100.0M + _config.BalanceProfitPercent) / 100.0M;
                _balanceLossAmount = _config.TestBalance * (100.0M -_config.BalanceLossPercent) / 100.0M;

                return true;
            }

            var balances = _tradingManager.GetBalances().GetAwaiter().GetResult();
            if (balances.IsNullOrEmpty())
                return false;

            decimal balance = balances.Sum(x => x.Available);
            _balanceProfitAmount = balance * (100.0M + _config.BalanceProfitPercent) / 100.0M;
            _balanceLossAmount = balance * (100.0M - _config.BalanceLossPercent) / 100.0M;

            return true;
        }

        private bool SetOrderProfitLossAmount()
        {
            if (_config.TestMode)
            {
                _roiOrderAmount = _config.TestBalance * (_config.ROIPercentage / 100.0M);
                _riskOrderAmount = _config.TestBalance * (_config.RiskPercentage / 100.0M);

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

        private void SetOrderTakeProfitAndStopLossPrice(Order order)
        {
            if (order == null) return;

            if (order.Side == OrderSide.Buy)
            {
                order.TakeProfitPrice = order.Price + _roiOrderAmount;
                order.StopLossPrice = order.Price - _riskOrderAmount;
            }
            else if (order.Side == OrderSide.Sell)
            {
                order.TakeProfitPrice = order.Price - _roiOrderAmount;
                order.StopLossPrice = order.Price + _riskOrderAmount;
            }
        }

        private void SetOrderRealizedProfitLossAmount(Order order)
        {
            if (order == null) return;

            if (order.Side == OrderSide.Buy)
            {
                order.RealizedProfitLossAmount = order.ExitPrice - order.Price; // xxx averagePrice
            }
            else if (order.Side == OrderSide.Sell)
            {
                order.RealizedProfitLossAmount = order.Price - order.ExitPrice;
            }

            if (_config.TestMode)
            {
                // we're in test mode. increase/decrease test balance
                _config.TestBalance += order.RealizedProfitLossAmount;
            }
        }

        private void DismissInvokeOrder()
        {
            if (_dismissInvokeOrder)
            {
                // already dismissed order invocation, nothing to do
                return;
            }

            if (_config.TestMode)
            {
                _dismissInvokeOrder = ReachedBalanceProfitLossAmount(_config.TestBalance);
                return;
            }

            var balances = _tradingManager.GetBalances().GetAwaiter().GetResult();
            if (balances.IsNullOrEmpty())
            {
                // no balances obtained, continue with order
                return;
            }

            _dismissInvokeOrder = ReachedBalanceProfitLossAmount(balances.Sum(x => x.Available));
        }

        private void StopOrderManager()
        {
            if (_stopOrderManager) return;

            //xxx for now only this stops order manager
            if (_dismissInvokeOrder && _orderBuffer.All(x => !x.IsActive)) //xxx implement force closure of all active orders
            {
                CloseWebSocketEventSubscription();

                ApplicationEvent?.Invoke(this, new OrderManagerEventArgs(EventType.TerminateApplication, "Balance profit/loss amount reached. Not allowed to trigger new orders. Terminating application."));

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
