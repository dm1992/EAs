using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using EATester.Classes;
using NQuotes;
using NQuotes.Arrays;

namespace EATester
{
    public class EAStrategy : MqlApi
    {
        #region Strategy properties

        private string _name = "MTTRADER_EA";
        private static bool _exitEA = false;
        private static bool _exitEANotified = false;
        private static bool _totalProfitLimitReset = false;
        private static bool _useMinutes = true;
        private static int _magicNumber = 159357;
        private static int _reportingDay = DateTime.Now.Day;
        private static DateTime _startTime = DateTime.Now;
        private static Settings _settings = new Settings();
        private static List<Order> _placedOrders = new List<Order>();
        private static List<double> _priceLevels = new List<double>();
        private static List<int> _reportedHours = new List<int>();
        private static double _startBalance;
        private static double _upperBalanceLimit;
        private static double _lowerBalanceLimit;
        private static int _currentBars;
        
        #endregion

        #region Main MQL API methods

        public override int init()
        {
            try
            {
                _startBalance = AccountBalance();
                _currentBars = Bars;
                _settings.Parse();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error '{ex.Message}' in init method. Using default settings.");
            }
            finally
            {
                _upperBalanceLimit = _startBalance * ((100.0 + _settings.TotalProfitPercent) / 100.0);
                _lowerBalanceLimit = _startBalance * ((100.0 - _settings.TotalLossPercent) / 100.0);

                ShowSettings();
                SendMail($"{_name} info", $"EA started executing with balance {_startBalance:F} EUR.");
                Console.WriteLine("-----------------------------------------------------------\n");
                Console.WriteLine($"EA started executing with balance {_startBalance:F} EUR.\n" +
                                  $"Upper / Lower balance limit {_upperBalanceLimit:F} EUR / {_lowerBalanceLimit:F} EUR.\n");
                Console.WriteLine("-----------------------------------------------------------");
            }

            return 0;
        }

        public override int deinit()
        {
            return 0;
        }

        public override int start()
        {
            if (!BeginTrade()) return 0;

            ResetTotalProfitLimit();
            RefreshRates();

            if (IsMarketRanging(out double adx))
            {
                NQLog.Warn($"Market is ranging. [ADX: {Math.Round(adx, 2)}]. Exiting pending orders...");
                DeletePendingMarketOrder(forceDelete: true);
                //ClosePositiveMarketOrder(); maybe we should leave them to cancel automatically.
            }
            else
            {
                PlaceMarketOrder();
            }
            
            ModifyMarketOrder();

            if (OrderLimitReached(out List<Order> buyOrders, out List<Order> sellOrders))
                CloseActiveMarketOrder(buyOrders, sellOrders);

            ClosePartialMarketOrder();
            DeletePendingMarketOrder();
            SendTradingReport();
            return 0;
        }

        #endregion

        #region Market order handler methods

        private void PlaceMarketOrder()
        {
            try
            {
                Order order = null;
                double price = 0.0;
                double movingAverage = iMA(null, 0, _settings.TrendPeriod, 0, MODE_SMA, PRICE_CLOSE, 0);
                int digits = (int)MarketInfo(Symbol(), MODE_DIGITS);

                // check if we have a new candle
                if (Bars - _currentBars > 0)
                {
                    _currentBars = Bars;
                    _priceLevels.Clear();
                }

                if (High[1] - Open[1] < _settings.TailSize && Close[1] < Open[1] && (Open[1] + 500 * Point) > movingAverage)
                {
                    price = Open[1] + 500 * Point;

                    if (!_priceLevels.Contains(price))
                    {
                        order = new Order()
                        {
                            Type = OP_BUYSTOP,
                            Price = price,
                            SL = NormalizeDouble(price - _settings.StopLoss * Point, digits),
                            TP = NormalizeDouble(price + _settings.TakeProfit1 * Point, digits)
                        };

                        if (SendOrder(ref order))
                        {
                            _placedOrders.Add(order);
                            _priceLevels.Add(price);
                            Console.WriteLine($"Placed order '{order.Ticket}' with price '{order.Price:F}'.");
                        }
                    }
                }

                if (High[1] - Open[1] > _settings.TailSize && Close[1] < Open[1] && (Open[1] + 500 * Point) > movingAverage)
                {
                    price = Close[2] > Open[2] ? Close[2] : Open[2];

                    if (!_priceLevels.Contains(price))
                    {
                        order = new Order()
                        {
                            Type = OP_BUYSTOP,
                            Price = price,
                            SL = NormalizeDouble(price - _settings.StopLoss * Point, digits),
                            TP = NormalizeDouble(price + _settings.TakeProfit1 * Point, digits)
                        };

                        if (SendOrder(ref order))
                        {
                            _placedOrders.Add(order);
                            _priceLevels.Add(price);
                            Console.WriteLine($"Placed order '{order.Ticket}' with price '{order.Price:F}'.");
                        }
                    }
                }

                if (Open[1] - Low[1] < _settings.TailSize && Close[1] > Open[1] && (Open[1] - 500 * Point) < movingAverage)
                {
                    price = Open[1] - 500 * Point;

                    if (!_priceLevels.Contains(price))
                    {
                        order = new Order()
                        {
                            Type = OP_SELLSTOP,
                            Price = price,
                            SL = NormalizeDouble(price + _settings.StopLoss * Point, digits),
                            TP = NormalizeDouble(price - _settings.TakeProfit1 * Point, digits)
                        };

                        if (SendOrder(ref order))
                        {
                            _placedOrders.Add(order);
                            _priceLevels.Add(price);
                            Console.WriteLine($"Placed order '{order.Ticket}' with price '{order.Price:F}'.");
                        }
                    }
                }

                if (Open[1] - Low[1] > _settings.TailSize && Close[1] > Open[1] && (Open[1] - 500 * Point) < movingAverage)
                {
                    price = Close[2] > Open[2] ? Open[2] : Close[2];

                    if (!_priceLevels.Contains(price))
                    {
                        order = new Order()
                        {
                            Type = OP_SELLSTOP,
                            Price = price,
                            SL = NormalizeDouble(price + _settings.StopLoss * Point, digits),
                            TP = NormalizeDouble(price - _settings.TakeProfit1 * Point, digits)
                        };

                        if (SendOrder(ref order))
                        {
                            _placedOrders.Add(order);
                            _priceLevels.Add(price);
                            Console.WriteLine($"Placed order '{order.Ticket}' with price '{order.Price:F}'.");
                        }
                    }
                }

                if (Close[1] > Open[1] && (Close[1] + 500 * Point) > movingAverage)
                {
                    price = Close[1] + 500 * Point;

                    if (!_priceLevels.Contains(price))
                    {
                        order = new Order()
                        {
                            Type = OP_BUYSTOP,
                            Price = price,
                            SL = NormalizeDouble(price - _settings.StopLoss * Point, digits),
                            TP = NormalizeDouble(price + _settings.TakeProfit1 * Point, digits)
                        };

                        if (SendOrder(ref order))
                        {
                            _placedOrders.Add(order);
                            _priceLevels.Add(price);
                            Console.WriteLine($"Placed order '{order.Ticket}' with price '{order.Price:F}'.");
                        }
                    }
                }

                if (Close[1] < Open[1] && (Close[1] - 500 * Point) < movingAverage)
                {
                    price = Close[1] - 500 * Point;

                    if (!_priceLevels.Contains(price))
                    {
                        order = new Order()
                        {
                            Type = OP_SELLSTOP,
                            Price = price,
                            SL = NormalizeDouble(price + _settings.StopLoss * Point, digits),
                            TP = NormalizeDouble(price - _settings.TakeProfit1 * Point, digits)
                        };

                        if (SendOrder(ref order))
                        {
                            _placedOrders.Add(order);
                            _priceLevels.Add(price);
                            Console.WriteLine($"Placed order '{order.Ticket}' with price '{order.Price:F}'.");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error '{ex.Message}' in place market order method.");
            }
        }

        private void ModifyMarketOrder()
        {
            List<Order> marketOrders = GetActiveOrders().Where(o => o.Operation == OrderOperation.Buy || o.Operation == OrderOperation.Sell).ToList();

            foreach (Order marketOrder in marketOrders)
            {
                double bid = MarketInfo(marketOrder.Name, MODE_BID);
                double point = MarketInfo(marketOrder.Name, MODE_POINT);
                double highPrice = iHigh(marketOrder.Name, PERIOD_H1, 1);
                double lowPrice = iLow(marketOrder.Name, PERIOD_H1, 1);
                int digits = (int)MarketInfo(marketOrder.Name, MODE_DIGITS);
                bool orderModify = false;

                if (marketOrder.Operation == OrderOperation.Buy)
                {
                    if (marketOrder.SL < marketOrder.Price && bid > NormalizeDouble(marketOrder.Price + _settings.BreakEven * point, digits))
                    {
                        NQLog.Debug($"BUY; BE point reached. Setting stoploss with price {marketOrder.Price}.");
                        marketOrder.SL = marketOrder.Price;
                        orderModify = true;
                    }
                    else if (lowPrice > marketOrder.Price)
                    {
                        NQLog.Debug($"BUY; Previous candle price point reached. Setting stoploss with price point {lowPrice}.");
                        marketOrder.SL = lowPrice;
                        orderModify = true;
                    }
                }
                else if (marketOrder.Operation == OrderOperation.Sell)
                {
                    if (marketOrder.SL > marketOrder.Price && bid < NormalizeDouble(marketOrder.Price - _settings.BreakEven * point, digits))
                    {
                        NQLog.Debug($"SELL; BE point reached. Setting stoploss with price {marketOrder.Price}.");
                        marketOrder.SL = marketOrder.Price;
                        orderModify = true;
                    }
                    else if (highPrice < marketOrder.Price)
                    {
                        NQLog.Debug($"SELL; Previous candle price point reached. Setting stoploss with price point {highPrice}.");
                        marketOrder.SL = highPrice;
                        orderModify = true;
                    }
                }

                if (orderModify)
                {
                    // check if order exists so far.
                    if (!OrderSelect(marketOrder.Ticket, SELECT_BY_TICKET))
                    {
                        NQLog.Warn($"({marketOrder.Name}) Order ticket {marketOrder.Ticket} seems closed. Can't modify order anymore.");
                        continue;
                    }

                    marketOrder.Expiration = OrderExpiration();
                    if (!ModifyOrder(marketOrder))
                    {
                        NQLog.Warn($"({marketOrder.Name}) Order ticket {marketOrder.Ticket} failed to modify.");
                        continue;
                    }

                    NQLog.Info($"({marketOrder.Name}) Order ticket {marketOrder.Ticket} successfully modified.");
                }
            }
        }

        private void ClosePositiveMarketOrder()
        {
            List<Order> marketOrders = GetActiveOrders().Where(o => o.Operation == OrderOperation.Buy || o.Operation == OrderOperation.Sell).ToList();

            foreach (Order marketOrder in marketOrders)
            {
                // check if order exists so far.
                if (!OrderSelect(marketOrder.Ticket, SELECT_BY_TICKET))
                {
                    NQLog.Warn($"({marketOrder.Name}) Order ticket {marketOrder.Ticket} seems already closed.");
                    continue;
                }

                if (OrderProfit() > 0)
                {
                    OrderClose(marketOrder.Ticket, marketOrder.Lots, OrderClosePrice(), 0);

                    if (ErrorOccurred())
                    {
                        NQLog.Warn($"({marketOrder.Name}) Order ticket {marketOrder.Ticket} failed to close.");
                        continue;
                    }

                    NQLog.Info($"({marketOrder.Name}) Order ticket {marketOrder.Ticket} successfully closed.");
                }
            }
        }

        private void ClosePartialMarketOrder()
        {
            List<Order> marketOrders = GetActiveOrders().Where(o => o.Operation == OrderOperation.Buy || o.Operation == OrderOperation.Sell).ToList();

            foreach (Order marketOrder in marketOrders)
            {
                int placedOrderIndex = -1;
                KeyValuePair<double, double> closureRate = default;

                // check if order exists so far.
                if (!OrderSelect(marketOrder.Ticket, SELECT_BY_TICKET))
                {
                    NQLog.Warn($"({marketOrder.Name}) Order ticket {marketOrder.Ticket} seems already closed.");
                    continue;
                }

                if (!GetPlacedOrderData(marketOrder, out placedOrderIndex, out closureRate)) continue;

                marketOrder.Lots = _placedOrders[placedOrderIndex].OriginalLots * closureRate.Value / 100;

                OrderClose(marketOrder.Ticket, marketOrder.Lots, OrderClosePrice(), 0);

                if (ErrorOccurred())
                {
                    NQLog.Warn($"({marketOrder.Name}) Order ticket {marketOrder.Ticket} failed to close.");
                    continue;
                }

                NQLog.Info($"({marketOrder.Name}) Order ticket {marketOrder.Ticket} successfully closed.");

                _placedOrders[placedOrderIndex].ClosureRates.Add(closureRate.Key, closureRate.Value);

                if (_placedOrders[placedOrderIndex].ClosureRates.Count == _settings.ClosureRates.Count)
                {
                    NQLog.Info($"({marketOrder.Name} Final partial closure level reached. Current order ticket ID {marketOrder.Ticket} is completely closed.");
                }
                else if (GetNewTicketId(marketOrder, out int newTicketId))
                {
                    NQLog.Info($"({marketOrder.Name} New ticket Id {newTicketId} from current order ticket ID {marketOrder.Ticket} successfully found.");
                    _placedOrders[placedOrderIndex].Ticket = newTicketId;
                }
                else
                {
                    NQLog.Warn($"({marketOrder.Name}) Failed to get new ticket ID from current order ticket ID {marketOrder.Ticket} on partial order close (Non-existing new ticketId ???).");
                }
            }
        }

        private void CloseActiveMarketOrder(List<Order> buyOrders, List<Order> sellOrders)
        {
            try
            {
                Order order;
                int ordersCount = buyOrders.Count > sellOrders.Count ?
                                  buyOrders.Count : sellOrders.Count;

                for (int index = 0; index < ordersCount; index++)
                {
                    if (buyOrders.ElementAtOrDefault(index) != null)
                    {
                        order = buyOrders[index];

                        if (CloseOrder(order))
                        {
                            _placedOrders.RemoveAll(o => o.Ticket == order.Ticket);
                            Console.WriteLine($"BUY order '{order.Ticket}' closed with price {order.ClosePrice:F} EUR.");
                        }
                    }

                    if (sellOrders.ElementAtOrDefault(index) != null)
                    {
                        order = sellOrders[index];

                        if (CloseOrder(order))
                        {
                            _placedOrders.RemoveAll(o => o.Ticket == order.Ticket);
                            Console.WriteLine($"SELL order '{order.Ticket}' closed with price {order.ClosePrice:F} EUR.");
                        }
                    }
                }

                if (!_exitEA && AccountBalance() >= _upperBalanceLimit) 
                    _exitEA = true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error '{ex.Message}' in close active market order method.");
            }
        }

        private void DeletePendingMarketOrder(bool forceDelete = false)
        {
            try
            {
                Order order;
                bool orderDelete;
                
                for (int index = OrdersTotal() - 1; index >= 0; index--)
                {
                    if (!OrderSelect(index, SELECT_BY_POS, MODE_TRADES)) 
                        continue;

                    orderDelete = false;
                    order = new Order()
                    {
                        Type = OrderType(),
                        Ticket = OrderTicket()
                    };

                    if (order.Type >= OP_BUYLIMIT && order.Type <= OP_SELLSTOP)
                    {
                        Order placedOrder = _placedOrders.First(o => o.Ticket == order.Ticket);

                        if (forceDelete || (placedOrder != null && (Bars - placedOrder.Bars > 0)))
                            orderDelete = true;

                        if (orderDelete && DeleteOrder(order))
                        {
                            _priceLevels.Remove(order.Price);
                            _placedOrders.RemoveAll(o => o.Ticket == order.Ticket);
                            Console.WriteLine($"Order '{order.Ticket}' deleted.");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error '{ex.Message}' in delete pending market order method.");
            }
        }

        private bool DissolveMarketOrder()
        {
            try
            {
                Order order;

                for (int index = OrdersTotal() - 1; index >= 0; index--)
                {
                    if (!OrderSelect(index, SELECT_BY_POS, MODE_TRADES))
                        continue;

                    order = new Order()
                    {
                        Type = OrderType(),
                        Ticket = OrderTicket()
                    };

                    if (order.Type >= OP_BUY && order.Type <= OP_SELL)
                    {
                        if (order.Type == OP_BUY)
                        {
                            order.Price = Bid;
                            order.ClosePrice = Bid - OrderOpenPrice();
                            order.Lots = OrderLots();
                        }
                        else if (order.Type == OP_SELL)
                        {
                            order.Price = Ask;
                            order.ClosePrice = OrderOpenPrice() - Ask;
                            order.Lots = OrderLots();
                        }

                        if (!CloseOrder(order)) return false;

                        _placedOrders.RemoveAll(o => o.Ticket == order.Ticket);
                        Console.WriteLine($"Order '{order.Ticket}' closed with price {order.ClosePrice:F} EUR.");
                    }
                    else if (order.Type >= OP_BUYLIMIT && order.Type <= OP_SELLSTOP)
                    {
                        if (!DeleteOrder(order)) return false;

                        _placedOrders.RemoveAll(o => o.Ticket == order.Ticket);
                        Console.WriteLine($"Order '{order.Ticket}' deleted.");
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error '{ex.Message}' in dissolve market order method.");
                return false;
            }
        }

        #endregion

        #region Order wrapper methods

        private bool SendOrder(ref Order order)
        {
            try
            {
                if (order == null) return false;

                int orderTicket = OrderSend(Symbol(), order.Type, _settings.LotSize, order.Price, 3, order.SL,
                                            order.TP, _name, _magicNumber, DateTime.MinValue, Color.Green);

                if (!ErrorOccurred() && orderTicket > 0)
                {
                    order.OriginalLots = _settings.LotSize;
                    order.Bars = Bars;
                    order.Ticket = orderTicket;
                    return true;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error '{ex.Message}' in send order method.");
            }

            return false;
        }

        private bool ModifyOrder(Order order)
        {
            try
            {
                if (order == null) return false;

                bool orderModified = OrderModify(order.Ticket, order.Price, order.SL, order.TP, order.Expiration);

                if (!ErrorOccurred() && orderModified)
                    return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error '{ex.Message}' in modify order method.");
            }

            return false;
        }

        private bool CloseOrder(Order order)
        {
            try
            {
                if (order == null) return false;

                bool orderClosed = OrderClose(order.Ticket, order.Lots, order.Price, 0);

                if (!ErrorOccurred() && orderClosed)
                    return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error '{ex.Message}' in close order method.");
            }

            return false;
        }

        private bool DeleteOrder(Order order)
        {
            try
            {
                if (order == null) return false;

                bool orderDeleted = OrderDelete(order.Ticket);

                if (!ErrorOccurred() && orderDeleted)
                    return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error '{ex.Message}' in delete order method.");
            }

            return false;
        }

        #endregion

        #region Helper methods

        private bool BeginTrade()
        {
            if (_exitEA)
            {
                if (!_exitEANotified)
                {
                    SendMail($"{_name} info", $"EA stopped executing with balance {AccountBalance():F} EUR.");
                    Console.WriteLine($"EA stopped executing with balance {AccountBalance():F} EUR.");

                    if (!DissolveMarketOrder())
                    {
                        SendMail($"{_name} error", $"Failed to dissolve pending/active market order.");
                        Console.WriteLine("Failed to dissolve pending/active market order.");
                    }

                    _exitEANotified = true;
                }

                return false;
            }

            DateTime currentTime = TimeCurrent();

            if (currentTime.Hour < _settings.OpenHour || currentTime.Hour >= _settings.CloseHour || Bars < 100 || !IsTradeAllowed())
                return false;

            if (currentTime.Hour == _settings.OpenHour && _useMinutes)
            {
                if (currentTime.Minute >= _settings.OpenMinute)
                {
                    _useMinutes = false;
                    return true;
                }

                return false;
            }

            return true;
        }

        private void ResetTotalProfitLimit()
        {
            if (!_totalProfitLimitReset && TimeCurrent().Hour >= _settings.TotalProfitPercentResetHour)
            {
                _upperBalanceLimit = AccountBalance() * ((100.0 + _settings.TotalProfitPercentAux) / 100.0);
                Console.WriteLine($"EA running too long. New upper balance limit is '{_upperBalanceLimit:F}' EUR.");
                _totalProfitLimitReset = true;
            }
        }

        private bool OrderLimitReached(out List<Order> buyOrders, out List<Order> sellOrders)
        {
            buyOrders = new List<Order>();
            sellOrders = new List<Order>();

            try
            {
                Order order;
                int activeOrders = 0;
                double totalOrderProfit = 0;
                double orderProfitLimit = 0;
                double totalOrderClosePrice = 0;
                double currentBalance = 0;

                for (int index = OrdersTotal() - 1; index >= 0; index--)
                {
                    if (!OrderSelect(index, SELECT_BY_POS, MODE_TRADES))
                        continue;

                    order = new Order()
                    {
                        Type = OrderType(),
                        Ticket = OrderTicket(),
                        Lots = OrderLots(),
                    };

                    if (order.Type >= OP_BUY && order.Type <= OP_SELL)
                    {
                        if (order.Type == OP_BUY)
                        {
                            order.Price = Bid;
                            order.ClosePrice = Bid - OrderOpenPrice();
                            buyOrders.Add(order);
                        }
                        else if (order.Type == OP_SELL)
                        {
                            order.Price = Ask;
                            order.ClosePrice = OrderOpenPrice() - Ask;
                            sellOrders.Add(order);
                        }

                        totalOrderProfit += OrderProfit();
                        totalOrderClosePrice += order.ClosePrice;
                        activeOrders++;
                    }
                }

                if (activeOrders > 0)
                {
                    orderProfitLimit = _startBalance * ((_settings.OrderProfitPercent + (activeOrders * _settings.OrderProfitWeight)) / 100);
                    currentBalance = AccountBalance() + totalOrderClosePrice;

                    if (currentBalance <= _lowerBalanceLimit) 
                        _exitEA = true;

                    if (_exitEA || totalOrderProfit >= orderProfitLimit)
                    {
                        buyOrders = buyOrders.OrderByDescending(o => Math.Abs(o.ClosePrice)).ToList();
                        sellOrders = sellOrders.OrderByDescending(o => Math.Abs(o.ClosePrice)).ToList();
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error '{ex.Message}' in order limit reached method.");
            }

            return false;
        }

        private bool ErrorOccurred()
        {
            int errorCode = GetLastError();

            if (errorCode > 1)
            {
                Alert($"Error '{errorCode}' occurred.");
                SendMail($"{_name} error", $"Error '{errorCode}' occurred.\nError details: '{ErrorDescription(errorCode)}'.");
                ResetLastError();
                return true;
            }

            return false;
        }

        private bool IsMarketRanging(out double adx)
        {
            adx = iADX(null, 0, 14, PRICE_CLOSE, MODE_MAIN, 0);
            return adx < 25;
        }

        private bool GetNewTicketId(Order order, out int newTicketId)
        {
            newTicketId = -1;

            if (order == null)
            {
                NQLog.Warn($"Order does not exist, can't get new ticket id.");
                return false;
            }

            for (int i = 0; i < OrdersTotal(); i++)
            {
                if (!OrderSelect(i, SELECT_BY_POS, MODE_TRADES)) continue;

                int orderTicketId = OrderTicket();

                if (orderTicketId > order.Ticket && OrderOpenTime() == order.OpenTime)
                {
                    newTicketId = orderTicketId;
                    return true;
                }
            }

            return false;
        }

        private bool GetPlacedOrderData(Order order, out int placedOrderIndex, out KeyValuePair<double, double> closureRate)
        {
            placedOrderIndex = -1;
            closureRate = default;

            if (order == null || order.ProfitPips <= 0) return false;

            placedOrderIndex = _placedOrders.FindIndex(o => o.Ticket == order.Ticket);

            if (placedOrderIndex == -1)
            {
                NQLog.Warn($"({order.Name}) Failed to get placed order index. Weird situation (partial order close issue?), invalid argument order ticket ID {order.Ticket} ???");
                return false;
            }

            foreach (var kvp in _settings.ClosureRates)
            {
                if (!_placedOrders[placedOrderIndex].ClosureRates.TryGetValue(kvp.Key, out double value) && order.ProfitPips >= kvp.Key)
                {
                    NQLog.Debug($"({order.Name}) Order profit pips {order.ProfitPips} reached order close profit pips {kvp.Key}. Using order close percentage: {kvp.Value}.");
                    closureRate = new KeyValuePair<double, double>(kvp.Key, kvp.Value);
                    return true;
                }
            }

            return false;
        }

        private void ShowSettings()
        {
            Console.WriteLine("-----------------------------------------------------------");
            Console.WriteLine(string.Format("OpenHour: {0}, \nOpenMinute: {1}, \nCloseHour: {2}, \nTakeProfit1: {3}, \n" +
                          "StopLoss: {4}, \nBreakEven: {5}, \nTrendPeriod: {6}, \nLotSize: {7}, \nTailSize: {8}, \nOrderProfitWeight: {9}, \n" +
                          "OrderProfitPercent: {10}, \nTotalProfitPercent: {11}, \nTotalLossPercent: {12}, \nTotalProfitPercentAux: {13}, \nTotalProfitPercentResetHour: {14}, \nReportingHours: {15}",
                          _settings.OpenHour, _settings.OpenMinute, _settings.CloseHour, _settings.TakeProfit1,
                          _settings.StopLoss, _settings.BreakEven, _settings.TrendPeriod, _settings.LotSize, _settings.TailSize, _settings.OrderProfitWeight,
                          _settings.OrderProfitPercent, _settings.TotalProfitPercent, _settings.TotalLossPercent, _settings.TotalProfitPercentAux, 
                          _settings.TotalProfitPercentResetHour, string.Join(",", _settings.ReportingHours)));
        }

        private TradingStats GetTradingStats()
        {
            TradingStats tradingStats = new TradingStats();

            for (int index = 0; index < OrdersHistoryTotal(); index++)
            {
                if (!OrderSelect(index, SELECT_BY_POS, MODE_HISTORY))
                    continue;

                if (OrderOpenTime() >= _startTime)
                {
                    if (OrderProfit() < 0)
                        tradingStats.LossTrades++;
                    else if (OrderProfit() > 0)
                        tradingStats.ProfitTrades++;
                    else
                        tradingStats.NeutralTrades++;

                    tradingStats.TotalTrades++;
                }
            }

            return tradingStats;
        }

        private List<Order> GetActiveOrders()
        {
            List<Order> activeOrders = new List<Order>();

            for (int i = 0; i < OrdersTotal(); i++)
            {
                if (!OrderSelect(i, SELECT_BY_POS, MODE_TRADES)) continue;

                Order activeOrder = new Order()
                {
                    Ticket = OrderTicket(),
                    Name = OrderSymbol(),
                    Operation = (OrderOperation)OrderType(),
                    TP = OrderTakeProfit(),
                    SL = OrderStopLoss(),
                    Price = OrderOpenPrice(),
                    OpenTime = OrderOpenTime(),
                    Comment = OrderComment(),
                    Lots = OrderLots()
                };

                if (activeOrder.Operation == OrderOperation.Buy)
                {
                    activeOrder.ProfitPips = (OrderClosePrice() - OrderOpenPrice()) / MarketInfo(OrderSymbol(), MODE_POINT);
                }
                else if (activeOrder.Operation == OrderOperation.Sell)
                {
                    activeOrder.ProfitPips = (OrderOpenPrice() - OrderClosePrice()) / MarketInfo(OrderSymbol(), MODE_POINT);
                }

                activeOrders.Add(activeOrder);
            }

            return activeOrders;
        }

        private void SendTradingReport()
        {
            try
            {
                DateTime currentTime = TimeCurrent();

                if (_reportingDay != DateTime.Now.Day)
                {
                    _reportedHours.Clear();
                    _reportingDay = DateTime.Now.Day;
                }

                if (_settings.ReportingHours.Contains(currentTime.Hour) && !_reportedHours.Contains(currentTime.Hour))
                {
                    TradingStats tradingStats = GetTradingStats();
                    _reportedHours.Add(currentTime.Hour);

                    string report = $"Total trades: {tradingStats.TotalTrades}\n" +
                                    $"Profit trades: {tradingStats.ProfitTrades}\n" +
                                    $"Loss trades: {tradingStats.LossTrades}\n" +
                                    $"Neutral trades: {tradingStats.NeutralTrades}\n" +
                                    $"Current balance: {AccountBalance():F} EUR";

                    SendMail($"{_name} trading report", report);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error '{ex.Message}' in send trading report method.");
            }
        }

        #endregion

    }
}