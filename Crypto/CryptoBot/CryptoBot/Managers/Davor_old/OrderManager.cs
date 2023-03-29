using Bybit.Net.Clients;
using Bybit.Net.Enums;
using Bybit.Net.Objects;
using Bybit.Net.Objects.Models;
using Bybit.Net.Objects.Models.Socket.Spot;
using Bybit.Net.Objects.Models.Spot.v1;
using Bybit.Net.Objects.Models.Spot.v3;
using CryptoBot.Data;
using CryptoBot.EventArgs;
using CryptoBot.Interfaces;
using CryptoExchange.Net.Sockets;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace CryptoBot.Managers.Davor_old
{
    public class OrderManager : IOrderManager
    {
        private readonly ITradingManager _tradingManager;
        private readonly IMarketManager _marketManager;
        private readonly Config _config;
        private readonly SemaphoreSlim _tickerSemaphore;
        private readonly CancellationTokenSource _monitorOrderStatsCts;

        private List<UpdateSubscription> _subscriptions;
        private List<BybitSpotOrderV1> _orders;

        public event EventHandler<ApplicationEventArgs> ApplicationEvent;

        public OrderManager(ITradingManager tradingManager, IMarketManager marketManager, Config config)
        {
            _tradingManager = tradingManager;
            _marketManager = marketManager;
            _config = config;
            _tickerSemaphore = new SemaphoreSlim(1, 1);
            _monitorOrderStatsCts = new CancellationTokenSource();

            _subscriptions = new List<UpdateSubscription>();
            _orders = new List<BybitSpotOrderV1>();

            Task.Run(() => MonitorOrderStatsThread(_monitorOrderStatsCts.Token));
        }

        public void InvokeAPISubscription()
        {
            ApplicationEvent?.Invoke(this, new ApplicationEventArgs(EventType.Information,
            $"Invoked API subscription in order manager."));

            BybitSocketClientOptions socketClientOptions = BybitSocketClientOptions.Default;
            socketClientOptions.SpotStreamsV1Options.BaseAddress = _config.SpotStreamEndpoint;

            BybitSocketClient socketClient = new BybitSocketClient(socketClientOptions);

            foreach (var symbol in _config.Symbols)
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

            if (_config.DelayedOrderInvoke)
            {
                BybitSpotOrderV1 lastSymbolOrder = _orders.Where(x => x.Symbol == symbol && x.IsWorking == false).OrderByDescending(x => x.UpdateTime).FirstOrDefault();
                if (lastSymbolOrder != null && (DateTime.Now - lastSymbolOrder.UpdateTime).TotalMinutes < _config.DelayOrderInvokeInMinutes)
                {
                    // wait some more time before new order is invoked
                    return false;
                }
            }

            if (!_marketManager.GetCurrentMarket(symbol, out IMarket market))
                return false;

            MarketDirection marketDirection = market.GetMarketDirection();
            if (marketDirection == MarketDirection.Unknown)
                return false;

            BybitSpotOrderV1 placedOrder = new BybitSpotOrderV1();
            placedOrder.Symbol = symbol;
            placedOrder.Side = marketDirection == MarketDirection.Buy ? OrderSide.Buy : OrderSide.Sell;
            placedOrder.Quantity = marketDirection == MarketDirection.Buy ? _config.BuyOpenQuantity : _config.SellOpenQuantity;

            if (!await _tradingManager.PlaceOrderAsync(placedOrder))
                return false;

            if (!_config.ApplicationTestMode)
            {
                BybitSpotOrderV1 order = await _tradingManager.GetOrderAsync(placedOrder.ClientOrderId);
                if (order == null)
                {
                    ApplicationEvent?.Invoke(this, new ApplicationEventArgs(EventType.Error,
                    $"!!!Unable to get order '{order.Id}' despite order was placed. Must delete placed order. Very strange!!!"));

                    return false;
                }

                DumpOrder(order, $"Invoked new order '{order.Id}'.");

                _orders.Add(order);
            }
            else 
            {
                // modify price due to incorrect test environment price
                decimal? lastPrice = await _tradingManager.GetPriceAsync(symbol);
                if (!lastPrice.HasValue)
                {
                    ApplicationEvent?.Invoke(this, new ApplicationEventArgs(EventType.Warning,
                    $"Failed to get last price for symbol '{symbol}'. Must delete placed order. Very strange."));

                    return false;
                }

                placedOrder.Price = lastPrice.Value;
                placedOrder.IsWorking = true;

                DumpOrder(placedOrder, $"Invoked new TEST order '{placedOrder.Id}'.");

                _orders.Add(placedOrder);
            }

            return true;
        }

        public async Task FinishOrderAsync(string symbol)
        {
            foreach (var order in _orders.Where(x => x.Symbol == symbol && x.IsWorking == true))
            {
                decimal? lastPrice = await _tradingManager.GetPriceAsync(symbol);
                if (!lastPrice.HasValue || lastPrice.Value == order.Price)
                {
                    // nothing to do
                    continue;
                }

                bool shouldFinish = false;
                //if (!ContinueOrder(order))
                //{
                //    shouldFinish = true;
                //}
                if (order.Side == OrderSide.Buy)
                {
                    if (lastPrice > order.Price) 
                    {
                        shouldFinish = true;
                    }
                    //else if (lastPrice <= order.Price - _config.SymbolStopLossAmount[symbol])
                    //{
                    //    shouldFinish = true;
                    //}
                }
                else if (order.Side == OrderSide.Sell)
                {
                    if (lastPrice < order.Price) 
                    {
                        shouldFinish = true;
                    }
                    //else if (lastPrice >= order.Price + _config.SymbolStopLossAmount[symbol])
                    //{
                    //    shouldFinish = true;
                    //}
                }

                if (shouldFinish)
                {
                    if (!_config.ApplicationTestMode)
                    {
                        // counter order used only for closing original order
                        BybitSpotOrderV1 placedCounterOrder = new BybitSpotOrderV1();
                        placedCounterOrder.Symbol = order.Symbol;
                        placedCounterOrder.Side = order.Side == OrderSide.Buy ? OrderSide.Sell : OrderSide.Buy;
                        placedCounterOrder.Quantity = order.Side == OrderSide.Buy ? order.QuantityFilled : order.QuoteQuantity;

                        if (!await _tradingManager.PlaceOrderAsync(placedCounterOrder))
                        {
                            ApplicationEvent?.Invoke(this, new ApplicationEventArgs(EventType.Warning,
                            $"Failed to place counter order '{placedCounterOrder.Id}'. Will try to finish order '{order.Id}' later."));

                            continue;
                        }

                        BybitSpotOrderV1 counterOrder = await _tradingManager.GetOrderAsync(placedCounterOrder.ClientOrderId);
                        if (counterOrder == null)
                        {
                            ApplicationEvent?.Invoke(this, new ApplicationEventArgs(EventType.Error,
                            $"!!!Unable to get counter order '{placedCounterOrder.Id}' despite counter order was placed. Very strange!!!"));
                        }

                        order.StopPrice = counterOrder?.AveragePrice;
                    }
                    else
                    {
                        // modify price due to incorrect test environment price
                        order.StopPrice = lastPrice.Value;
                    }
                    
                    order.UpdateTime = DateTime.Now;
                    order.IsWorking = false;

                    DumpOrder(order, $"Finished invoked order '{order.Id}'.");

                    HandleFinishedOrder(order);

                    //xxx is needed balance reached? Close API subscription connection.
                }
            }
        }

        private bool ContinueOrder(BybitSpotOrderV1 order)
        {
            if (order == null) return false;

            // refresh market information
            if (!_marketManager.GetCurrentMarket(order.Symbol, out IMarket market))
                return false;

            if (order.Side == OrderSide.Buy)
            {
                if (market.GetMarketDirection() == MarketDirection.Buy)
                {
                    return true;
                }
            }
            else if (order.Side == OrderSide.Sell)
            {
                if (market.GetMarketDirection() == MarketDirection.Sell)
                {
                    return true;
                }
            }

            return false;
        }

        private void DumpOrder(BybitSpotOrderV1 order, string comment = "N/A")
        {
            if (order == null) return;

            ApplicationEvent?.Invoke(this, new ApplicationEventArgs(EventType.Information,
            $"[{(comment)}] '{order.Id}' ('{order.ClientOrderId}'), '{order.Symbol}', '{order.Price}', '{order.Quantity}', " +
            $"'{order.Type}', '{order.Side}', '{order.Status}', '{order.TimeInForce}', " +
            $"'{order.QuantityFilled}', '{order.QuoteQuantity}', '{order.AveragePrice}', '{order.StopPrice}', " +
            $"'{order.IcebergQuantity}', '{order.CreateTime}', '{order.UpdateTime}', '{order.IsWorking}'."));
        }

        private void HandleFinishedOrder(BybitSpotOrderV1 order)
        {
            if (order?.StopPrice == null)
            {
                // order not finished yet or problem happened
                return;
            }

            if (order.Side == OrderSide.Buy)
            {
                order.StopPrice = order.StopPrice.Value - order.Price; // xxx averagePrice
            }
            else if (order.Side == OrderSide.Sell)
            {
                order.StopPrice = order.Price - order.StopPrice.Value;
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

                    lock (_orders)
                    {
                        foreach (var symbol in _config.Symbols)
                        {
                            ApplicationEvent?.Invoke(this, new ApplicationEventArgs(EventType.Information,
                            $"\n------------------------\n" +
                            $"SYMBOL: '{symbol}'\n" +
                            $"------------------------\n" +
                            $"ACTIVE orders: '{_orders.Where(x => x.Symbol == symbol && x.IsWorking == true).Count()}',\n" +
                            $"PROFIT orders: '{_orders.Where(x => x.Symbol == symbol && x.IsWorking != true && x.StopPrice > 0).Count()}',\n" +
                            $"LOSS orders: '{_orders.Where(x => x.Symbol == symbol && x.IsWorking != true && x.StopPrice < 0).Count()}'\n" +
                            $"NEUTRAL orders: '{_orders.Where(x => x.Symbol == symbol && x.IsWorking != true && x.StopPrice == 0).Count()}'\n" +
                            $"SYMBOL BALANCE: '{_orders.Where(x => x.Symbol == symbol).Sum(x => x.StopPrice ?? 0)}'\n" +
                            $"------------------------\n"));
                        }

                        ApplicationEvent?.Invoke(this, new ApplicationEventArgs(EventType.Information,
                        $"\n------------------------\n" +
                        $"TOTAL BALANCE: '{_orders.Sum(x => x.StopPrice ?? 0)}'\n" +
                        $"------------------------\n"));
                    }
                }
                catch (Exception e)
                {
                    ApplicationEvent?.Invoke(this, new ApplicationEventArgs(EventType.Error,
                    $"!!!MonitorOrderStatsThread failed!!! {e}"));
                }

                Task.Delay(30000).Wait();
            }
        }


        #region Event handlers

        private void API_Subscription_ConnectionRestored(TimeSpan obj)
        {
            ApplicationEvent?.Invoke(this, new ApplicationEventArgs(EventType.Information,
            $"API subscription connection restored in order manager."));
        }

        private void API_Subscription_ConnectionLost()
        {
            ApplicationEvent?.Invoke(this, new ApplicationEventArgs(EventType.Information,
            $"API subscription connection lost in order manager.")); 
        }

        private void API_Subscription_ConnectionClosed()
        {
            ApplicationEvent?.Invoke(this, new ApplicationEventArgs(EventType.Information,
            $"API subscription connection closed in order manager."));
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
                $"!!!HandleTicker failed!!! {e}"));
            }
            finally
            {
                _tickerSemaphore.Release();
            }
        }

        #endregion
    }
}
