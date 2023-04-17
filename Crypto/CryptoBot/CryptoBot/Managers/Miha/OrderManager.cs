using Bybit.Net.Clients;
using Bybit.Net.Enums;
using Bybit.Net.Objects;
using Bybit.Net.Objects.Models.Socket.Spot;
using Bybit.Net.Objects.Models.Spot.v3;
using CryptoBot.Data;
using CryptoBot.EventArgs;
using CryptoBot.Interfaces.Managers;
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
        private readonly ITradingManager _tradingAPIManager;
        private readonly Config _config;
        private readonly SemaphoreSlim _tickerSemaphore;
        private readonly CancellationTokenSource _monitorOrderStatsCts;

        private List<BybitSpotOrderV3> _orders;
        private bool _isInitialized;

        public event EventHandler<ApplicationEventArgs> ApplicationEvent;

        public OrderManager(ITradingManager tradingAPIManager, Config config)
        {
            _tradingAPIManager = tradingAPIManager;
            _config = config;

            _tickerSemaphore = new SemaphoreSlim(1, 1);
            _monitorOrderStatsCts = new CancellationTokenSource();
            _orders = new List<BybitSpotOrderV3>();
            _isInitialized = false;
        }

        public bool Initialize()
        {
            try
            {
                if (_isInitialized) return true;

                Task.Run(() => MonitorOrderStatsThread(_monitorOrderStatsCts.Token));

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

        public async Task<bool> InvokeOrder(string symbol, MarketDirection marketDirection)
        {
            if (_orders.Where(x => x.Symbol == symbol && x.IsWorking == true).Count() >= _config.ActiveSymbolOrders)
            {
                // wait for order(s) to finish
                return false;
            }

            BybitSpotOrderV3 placedOrder = new BybitSpotOrderV3();
            placedOrder.Symbol = symbol;
            placedOrder.Type = OrderType.Market; //xxx
            placedOrder.Side = marketDirection == MarketDirection.Uptrend ? OrderSide.Buy : OrderSide.Sell;
            placedOrder.Quantity = marketDirection == MarketDirection.Uptrend ? _config.BuyOrderVolume : _config.SellOrderVolume;

            if (!await _tradingAPIManager.PlaceOrder(placedOrder))
                return false;

            if (!_config.TestMode)
            {
                BybitSpotOrderV3 order = await _tradingAPIManager.GetOrder(placedOrder.ClientOrderId);
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
                decimal? lastPrice = await _tradingAPIManager.GetPrice(symbol);
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

        public async Task FinishOrder(string symbol)
        {
            foreach (var order in _orders.Where(x => x.Symbol == symbol && x.IsWorking == true))
            {
                decimal? lastPrice = await _tradingAPIManager.GetPrice(symbol);
                if (!lastPrice.HasValue || lastPrice.Value == order.Price)
                {
                    // nothing to do
                    continue;
                }

                bool shouldFinish = false; //xxx determine price levels - TP = SL (max 5 price levels distance)

                if (shouldFinish)
                {
                    if (!_config.TestMode)
                    {
                        // counter order used only for closing original order
                        BybitSpotOrderV3 placedCounterOrder = new BybitSpotOrderV3();
                        placedCounterOrder.Symbol = order.Symbol;
                        placedCounterOrder.Side = order.Side == OrderSide.Buy ? OrderSide.Sell : OrderSide.Buy;
                        placedCounterOrder.Quantity = order.Side == OrderSide.Buy ? order.QuantityFilled : order.QuoteQuantity;

                        if (!await _tradingAPIManager.PlaceOrder(placedCounterOrder))
                        {
                            ApplicationEvent?.Invoke(this, new ApplicationEventArgs(EventType.Warning,
                            $"Failed to place counter order '{placedCounterOrder.Id}'. Will try to finish order '{order.Id}' later."));

                            continue;
                        }

                        BybitSpotOrderV3 counterOrder = await _tradingAPIManager.GetOrder(placedCounterOrder.ClientOrderId);
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

        private void DumpOrder(BybitSpotOrderV3 order, string comment = "N/A")
        {
            if (order == null) return;

            ApplicationEvent?.Invoke(this, new ApplicationEventArgs(EventType.Information,
            $"[{(comment)}] '{order.Id}' ('{order.ClientOrderId}'), '{order.Symbol}', '{order.Price}', '{order.Quantity}', " +
            $"'{order.Type}', '{order.Side}', '{order.Status}', '{order.TimeInForce}', " +
            $"'{order.QuantityFilled}', '{order.QuoteQuantity}', '{order.AveragePrice}', '{order.StopPrice}', " +
            $"'{order.IcebergQuantity}', '{order.CreateTime}', '{order.UpdateTime}', '{order.IsWorking}'."));
        }

        private void HandleFinishedOrder(BybitSpotOrderV3 order)
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
    }
}
