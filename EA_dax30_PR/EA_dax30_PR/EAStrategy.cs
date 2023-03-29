using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using EATester.Classes;
using NQuotes;


namespace EATester
{
    public class EAStrategy : MqlApi
    {
        private string _name = "DM_EA";
        private static bool _exitEA = false;
        private static int _magicNumber = 159357;
        private static double _startBalance;
        private static double _tradeGrowthLimit;
        private static DateTime _startTime;
        private static Settings _settings = new Settings();
        private static List<Order> _placedOrders = new List<Order>();
        private static List<double> _priceLevels = new List<double>();
        private static List<int> _reportedHours = new List<int>();
        private static int _reportingDay;
        private static int _currentBars;
        private static bool _useMinutes;

        public override int init()
        {
            try
            {
                _startBalance = AccountBalance();
                _startTime = DateTime.Now;
                _reportingDay = DateTime.Now.Day;
                _currentBars = Bars;
                _useMinutes = true;
                _settings.Parse();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error '{ex.Message}' in init method. Using default settings...");
            }
            finally
            {
                _tradeGrowthLimit = _settings.UseRegularTradeGrowth ?
                                    _startBalance * (_settings.TradeGrowthPercent / 100.0) : _settings.TradeGrowthPips;
                ShowSettings();
                SendMail($"{_name} info", $"EA initialized and started executing.");
            }

            return 0;
        }

        public override int deinit()
        {
            return 0;
        }

        public override int start()
        {
            try
            {
                if (!_exitEA)
                {
                    RefreshRates();
                    PlaceMarketOrder();
                    CloseMarketOrder();
                    HandleMarketOrder();

                    if (BalanceLimitReached(out double limit))
                    {
                        _exitEA = true;
                        SendMail($"{_name} info", $"EA stopped executing.\nBalance limit '{limit}' reached.");
                        Console.WriteLine($"EA stopped executing. Balance limit '{limit}' reached.");
                        CloseMarketOrder(_exitEA); // close opened orders on EA exit
                    }

                    SendTradingReport();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error '{ex.Message}' in start method.");
            }

            return 0;
        }

        private bool BeginTrade()
        {
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

        private void PlaceMarketOrder()
        {
            try
            {
                if (!BeginTrade()) return;

                Order order = null;
                double price = 0.0;
                double movingAverage = iMA(null, 0, _settings.TrendPeriod, 0, MODE_SMA, PRICE_CLOSE, 0);

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
                            SL = NormalizeDouble(price - _settings.StopLoss * Point, 5),
                            TP = NormalizeDouble(price + _settings.TakeProfit1 * Point, 5)
                        };

                        if (SendOrder(ref order))
                        {
                            _placedOrders.Add(order);
                            _priceLevels.Add(price);
                            Console.WriteLine($"Placed order '{order.Ticket}' with price '{order.Price}'.");
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
                            SL = NormalizeDouble(price - _settings.StopLoss * Point, 5),
                            TP = NormalizeDouble(price + _settings.TakeProfit1 * Point, 5)
                        };

                        if (SendOrder(ref order))
                        {
                            _placedOrders.Add(order);
                            _priceLevels.Add(price);
                            Console.WriteLine($"Placed order '{order.Ticket}' with price '{order.Price}'.");
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
                            SL = NormalizeDouble(price + _settings.StopLoss * Point, 5),
                            TP = NormalizeDouble(price - _settings.TakeProfit1 * Point, 5)
                        };

                        if (SendOrder(ref order))
                        {
                            _placedOrders.Add(order);
                            _priceLevels.Add(price);
                            Console.WriteLine($"Placed order '{order.Ticket}' with price '{order.Price}'.");
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
                            SL = NormalizeDouble(price + _settings.StopLoss * Point, 5),
                            TP = NormalizeDouble(price - _settings.TakeProfit1 * Point, 5)
                        };

                        if (SendOrder(ref order))
                        {
                            _placedOrders.Add(order);
                            _priceLevels.Add(price);
                            Console.WriteLine($"Placed order '{order.Ticket}' with price '{order.Price}'.");
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
                            SL = NormalizeDouble(price - _settings.StopLoss * Point, 5),
                            TP = NormalizeDouble(price + _settings.TakeProfit1 * Point, 5)
                        };

                        if (SendOrder(ref order))
                        {
                            _placedOrders.Add(order);
                            _priceLevels.Add(price);
                            Console.WriteLine($"Placed order '{order.Ticket}' with price '{order.Price}'.");
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
                            SL = NormalizeDouble(price + _settings.StopLoss * Point, 5),
                            TP = NormalizeDouble(price - _settings.TakeProfit1 * Point, 5)
                        };

                        if (SendOrder(ref order))
                        {
                            _placedOrders.Add(order);
                            _priceLevels.Add(price);
                            Console.WriteLine($"Placed order '{order.Ticket}' with price '{order.Price}'.");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error '{ex.Message}' in place market order method.");
            }
        }

        private bool SendOrder(ref Order order)
        {
            try
            {
                if (order == null) return false;

                int orderTicket = OrderSend(Symbol(), order.Type, _settings.LotSize, order.Price, 3, order.SL,
                                            order.TP, _name, _magicNumber, DateTime.MinValue, Color.Green);

                if (!ErrorOccurred() && orderTicket > 0)
                {
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

        private void CloseMarketOrder(bool exitEA = false)
        {
            bool orderClosed;
            bool orderDeleted;
            int orderType;
            int orderTicket;

            try
            {
                if (!exitEA && !TradeGrowthLimitReached()) 
                    return;

                for (int index = OrdersTotal() - 1; index >= 0; index--)
                {
                    if (!OrderSelect(index, SELECT_BY_POS, MODE_TRADES))
                        continue;

                    orderType = OrderType();
                    orderTicket = OrderTicket();

                    if (orderType == OP_BUY || orderType == OP_SELL)
                    {
                        orderClosed = OrderClose(orderTicket, OrderLots(), MarketInfo(Symbol(), MODE_BID), 3);

                        if (!ErrorOccurred() && orderClosed)
                        {
                            _placedOrders.RemoveAll(o => o.Ticket == orderTicket);
                            Console.WriteLine($"Order '{orderTicket}' closed.");
                        }
                    }
                    else if (exitEA)
                    {
                        // delete pending orders on EA exit
                        orderDeleted = OrderDelete(orderTicket);

                        if (!ErrorOccurred() && orderDeleted)
                        {
                            _placedOrders.RemoveAll(o => o.Ticket == orderTicket);
                            Console.WriteLine($"Order '{orderTicket}' deleted.");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error '{ex.Message}' in close market order method.");
            }
        }

        private bool TradeGrowthLimitReached()
        {
            double totalTradeProfit = _settings.UseRegularTradeGrowth ?
                                      GetOrderPriceProfit() : GetOrderPipProfit();

            return totalTradeProfit >= _tradeGrowthLimit;
        }

        private double GetOrderPriceProfit()
        {
            double totalOrderPriceProfit = 0.0;

            for (int index = OrdersTotal() - 1; index >= 0; index--)
            {
                if (!OrderSelect(index, SELECT_BY_POS, MODE_TRADES))
                    continue;

                // look profits on all type of orders
                totalOrderPriceProfit += OrderProfit();
            }

            return totalOrderPriceProfit;
        }

        private double GetOrderPipProfit()
        {
            int orderType;
            double totalOrderPipProfit = 0.0;

            for (int index = OrdersTotal() - 1; index >= 0; index--)
            {
                if (!OrderSelect(index, SELECT_BY_POS, MODE_TRADES))
                    continue;

                orderType = OrderType();

                if (orderType == OP_BUY)
                    totalOrderPipProfit += (MarketInfo(OrderSymbol(), MODE_BID) - OrderOpenPrice()) / MarketInfo(OrderSymbol(), MODE_POINT);
                else if (orderType == OP_SELL)
                    totalOrderPipProfit += (OrderOpenPrice() - MarketInfo(OrderSymbol(), MODE_ASK)) / MarketInfo(OrderSymbol(), MODE_POINT);
            }

            return totalOrderPipProfit;
        }

        private void HandleMarketOrder()
        {
            try
            {
                bool orderModify;
                bool orderModified;
                bool orderDelete;
                bool orderDeleted;
                double orderOpenPrice;
                double orderStopLoss;
                int orderTicket;
                int orderType;
                Order order;

                if (_settings.BreakEven > 0)
                {
                    for (int index = OrdersTotal() - 1; index >= 0; index--)
                    {
                        if (!OrderSelect(index, SELECT_BY_POS, MODE_TRADES))
                            continue;

                        orderModify = false;
                        orderDelete = false;
                        orderOpenPrice = OrderOpenPrice();
                        orderStopLoss = OrderStopLoss();
                        orderTicket = OrderTicket();
                        orderType = OrderType();

                        if (orderType == OP_BUY)
                        {
                            if (Bid > NormalizeDouble(orderOpenPrice + _settings.BreakEven * Point, 5))
                            {
                                orderModify = true;
                                orderStopLoss = orderOpenPrice;
                            }
                        }
                        else if (orderType == OP_SELL)
                        {
                            if (Bid < NormalizeDouble(orderOpenPrice - _settings.BreakEven * Point, 5))
                            {
                                orderModify = true;
                                orderStopLoss = orderOpenPrice;
                            }
                        }
                        else
                        {
                            // pending order
                            order = _placedOrders.First(o => o.Ticket == orderTicket);

                            if (order != null && (Bars - order.Bars > 0))
                            {
                                orderDelete = true;
                            }
                        }

                        if (orderModify)
                        {
                            orderModified = OrderModify(orderTicket, orderOpenPrice, orderStopLoss, OrderTakeProfit(), OrderExpiration());

                            if (!ErrorOccurred() && orderModified)
                            {
                                // update price for order ticket
                                _placedOrders.First(o => o.Ticket == orderTicket).Price = orderOpenPrice;
                            }

                        }
                        else if (orderDelete)
                        {
                            orderDeleted = OrderDelete(orderTicket);

                            if (!ErrorOccurred() && orderDeleted)
                            {
                                // delete order with order ticket
                                _placedOrders.RemoveAll(o => o.Ticket == orderTicket);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error '{ex.Message}' in handle market order method.");
            }
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

        private bool BalanceLimitReached(out double limit)
        {
            limit = 0.0;
            double balance = AccountBalance();
            double lowerBalanceLimit = _startBalance * ((100.0 - _settings.LossPercent) / 100.0);
            double upperBalanceLimit = _startBalance * ((100.0 + _settings.ProfitPercent) / 100.0);

            if (balance <= lowerBalanceLimit)
            {
                limit = lowerBalanceLimit;
                return true;
            }
            else if (balance >= upperBalanceLimit)
            {
                limit = upperBalanceLimit;
                return true;
            }

            return false;
        }

        private void ShowSettings()
        {
            Console.WriteLine(string.Format("EA Settings\nOpenHour: {0}, \nOpenMinute: {1}, \nCloseHour: {2}, \nTakeProfit1: {3}, \n" +
                          "StopLoss: {4}, \nBreakEven: {5}, \nTrendPeriod: {6}, \nMaxDayLosses: {7}, \nLotSize: {8}, \nTailSize: {9}, \n" +
                          "ProfitPercent: {10}, \nLossPercent: {11}, \nUseRegularTradeGrowth: {12}, \nTradeGrowthPercent: {13}, \nTradeGrowthPips: {14}, \nReportingHours: {15}",
                          _settings.OpenHour, _settings.OpenMinute, _settings.CloseHour, _settings.TakeProfit1,
                          _settings.StopLoss, _settings.BreakEven, _settings.TrendPeriod, _settings.MaxDayLosses,
                          _settings.LotSize, _settings.TailSize, _settings.ProfitPercent, _settings.LossPercent, 
                          _settings.UseRegularTradeGrowth, _settings.TradeGrowthPercent, _settings.TradeGrowthPips, string.Join(",", _settings.ReportingHours)));
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
                                    $"Balance: {AccountBalance()}";

                    SendMail($"{_name} trading report", report);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error '{ex.Message}' in send trading report method.");
            }
        }
    }
}