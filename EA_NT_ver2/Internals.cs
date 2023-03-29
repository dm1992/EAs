using EA.Data;
using NQuotes;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;

namespace EA
{
    /// <summary>
    /// Strategy for trading currency pairs on M15 timeframe.
    /// </summary>
    partial class Strategy
    {
        const int PLACE_ORDER_ATTEMPTS = 20;

        private Settings _settings;
        private List<TradingSymbol> _tradingSymbols;
        private List<Order> _placedOrders;
        private EventWaitHandle tickActive = new EventWaitHandle(false, EventResetMode.ManualReset);

        void StartTrading()
        {
            try
            {
                if (tickActive.WaitOne(0))
                {
                    // we're already in trading tick action.
                    return;
                }

                tickActive.Set();

                RefreshRates();

                ModifyMarketOrders();

                CloseMarketOrders(partialClosure: true);

                OpenMarketOrders();
            }
            finally
            {
                tickActive.Reset();
            }
        }

        void ModifyMarketOrders()
        {
            List<Order> marketOrders = GetActiveOrders().Where(o => o.Operation == OrderOperation.Buy || o.Operation == OrderOperation.Sell).ToList();

            foreach (Order marketOrder in marketOrders)
            {
                double bid = MarketInfo(marketOrder.SymbolName, MODE_BID);
                double point = MarketInfo(marketOrder.SymbolName, MODE_POINT);
                double highPrice = iHigh(marketOrder.SymbolName, PERIOD_M15, 1);
                double lowPrice = iLow(marketOrder.SymbolName, PERIOD_M15, 1);
                int digits = (int)MarketInfo(marketOrder.SymbolName, MODE_DIGITS);
                bool orderModify = false;

                if (marketOrder.Operation == OrderOperation.Buy)
                {
                    if (marketOrder.StopLoss < marketOrder.EntryPrice && bid > NormalizeDouble(marketOrder.EntryPrice + _settings.BreakEven * point, digits))
                    {
                        NQLog.Debug($"BUY; BE point reached. Setting stoploss with price {marketOrder.EntryPrice}.");
                        marketOrder.StopLoss = marketOrder.EntryPrice;
                        orderModify = true;
                    }
                    else if (lowPrice > marketOrder.EntryPrice)
                    {
                        NQLog.Debug($"BUY; Previous candle price point reached. Setting stoploss with price point {lowPrice}.");
                        marketOrder.StopLoss = lowPrice;
                        orderModify = true;
                    }
                }
                else if (marketOrder.Operation == OrderOperation.Sell)
                {
                    if (marketOrder.StopLoss > marketOrder.EntryPrice && bid < NormalizeDouble(marketOrder.EntryPrice - _settings.BreakEven * point, digits))
                    {
                        NQLog.Debug($"SELL; BE point reached. Setting stoploss with price {marketOrder.EntryPrice}.");
                        marketOrder.StopLoss = marketOrder.EntryPrice;
                        orderModify = true;
                    }
                    else if (highPrice < marketOrder.EntryPrice)
                    {
                        NQLog.Debug($"SELL; Previous candle price point reached. Setting stoploss with price point {highPrice}.");
                        marketOrder.StopLoss = highPrice;
                        orderModify = true;
                    }
                }

                if (orderModify)
                {
                    // check if order exists so far.
                    if (!OrderSelect(marketOrder.TicketId, SELECT_BY_TICKET))
                    {
                        NQLog.Warn($"({marketOrder.SymbolName}) Order ticket {marketOrder.TicketId} seems closed. Can't modify order anymore.");
                        continue;
                    }

                    OrderModify(marketOrder.TicketId, marketOrder.EntryPrice, marketOrder.StopLoss, marketOrder.TakeProfit, OrderExpiration());

                    if (ErrorOccurred())
                    {
                        NQLog.Warn($"({marketOrder.SymbolName}) Order ticket {marketOrder.TicketId} failed to modify.");
                        continue;
                    }

                    NQLog.Info($"({marketOrder.SymbolName}) Order ticket {marketOrder.TicketId} successfully modified.");
                }
            }
        }

        void CloseMarketOrders(bool partialClosure = false)
        {
            List<Order> marketOrders = GetActiveOrders().Where(o => o.Operation == OrderOperation.Buy || o.Operation == OrderOperation.Sell).ToList();

            foreach (Order marketOrder in marketOrders)
            {
                int placedOrderIndex = -1;
                KeyValuePair<double, double> closureRate = default;

                // check if order exists so far.
                if (!OrderSelect(marketOrder.TicketId, SELECT_BY_TICKET))
                {
                    NQLog.Warn($"({marketOrder.SymbolName}) Order ticket {marketOrder.TicketId} seems already closed.");
                    continue;
                }

                if (partialClosure)
                {
                    if (!GetPlacedOrderData(marketOrder, out placedOrderIndex, out closureRate)) continue;

                    marketOrder.Lots = _placedOrders[placedOrderIndex].OriginalLots * closureRate.Value / 100;
                }

                OrderClose(marketOrder.TicketId, marketOrder.Lots, OrderClosePrice(), 0);

                if (ErrorOccurred())
                {
                    NQLog.Warn($"({marketOrder.SymbolName}) Order ticket {marketOrder.TicketId} failed to close.");
                    continue;
                }

                NQLog.Info($"({marketOrder.SymbolName}) Order ticket {marketOrder.TicketId} successfully closed.");

                if (partialClosure)
                {
                    _placedOrders[placedOrderIndex].ClosureRates.Add(closureRate.Key, closureRate.Value);

                    if (_placedOrders[placedOrderIndex].ClosureRates.Count == _settings.ClosureRates.Count)
                    {
                        NQLog.Info($"({marketOrder.SymbolName} Final partial closure level reached. Current order ticket ID {marketOrder.TicketId} is completely closed.");
                    }
                    else if (GetNewTicketId(marketOrder, out int newTicketId))
                    {
                        NQLog.Info($"({marketOrder.SymbolName} New ticket Id {newTicketId} from current order ticket ID {marketOrder.TicketId} successfully found.");
                        _placedOrders[placedOrderIndex].TicketId = newTicketId;
                    }
                    else
                    {
                        NQLog.Warn($"({marketOrder.SymbolName}) Failed to get new ticket ID from current order ticket ID {marketOrder.TicketId} on partial order close (Non-existing new ticketId ???).");
                    }
                }
            }
        }

        void OpenMarketOrders()
        {
            foreach (TradingSymbol tradingSymbol in _tradingSymbols)
            {
                if (!IsTradingTimeValid(tradingSymbol) || IsMarketOrderActive(tradingSymbol))
                    continue;

                if (CreateOrder(tradingSymbol, out Order order))
                    PlaceOrder(order);
            }
        }

        bool IsTradingTimeValid(TradingSymbol tradingSymbol)
        {
            //if (tradingSymbol == null) return false;

            //DateTime currentTime = TimeCurrent();
            //foreach (TimeInterval ti in tradingSymbol.TimeIntervals)
            //{
            //    if (currentTime.Hour >= ti.FromHour && currentTime.Hour <= ti.ToHour)
            //    {
            //        if (currentTime.Hour == ti.FromHour && currentTime.Minute < ti.FromMinute)
            //            return false;
            //        else if (currentTime.Hour == ti.ToHour && currentTime.Minute > ti.ToMinute)
            //            return false;
            //        return true;
            //    }
            //}

            //return false;

            return true;
        }

        bool IsMarketOrderActive(TradingSymbol tradingSymbol)
        {
            if (tradingSymbol == null)
                return false;

            return GetActiveOrders().Where(o => o.SymbolName == tradingSymbol.Name).Any();
        }

        bool CreateOrder(TradingSymbol tradingSymbol, out Order order)
        {
            order = null;

            try
            {
                double slowMovingAverageCurr = iMA(tradingSymbol.Name, PERIOD_H1, 20, 0, MODE_EMA, PRICE_CLOSE, 0);
                double fastMovingAverageCurr = iMA(tradingSymbol.Name, PERIOD_H1, 8, 0, MODE_EMA, PRICE_CLOSE, 0);
                double point = MarketInfo(tradingSymbol.Name, MODE_POINT);
                int digits = (int)MarketInfo(tradingSymbol.Name, MODE_DIGITS);
                double entryPrice = 0;

                if ((fastMovingAverageCurr > slowMovingAverageCurr) && (MathAbs(fastMovingAverageCurr - slowMovingAverageCurr) > 0.00005))
                {
                    // xxx buy in case rsi is growing
                    if (iRSI(tradingSymbol.Name, PERIOD_M15, 14, PRICE_CLOSE, 0) > 60)
                    {
                        entryPrice = iHigh(tradingSymbol.Name, PERIOD_M15, 1) + 1 * point;
                        order = new Order()
                        {
                            SymbolName = tradingSymbol.Name,
                            Operation = OrderOperation.BuyStop,
                            OriginalLots = _settings.LotSize,
                            Lots = _settings.LotSize,
                            EntryPrice = entryPrice,
                            StopLoss = NormalizeDouble(entryPrice - _settings.StopLoss * point, digits),
                            TakeProfit = NormalizeDouble(entryPrice + _settings.TakeProfit * point, digits),
                            Color = Color.Green
                        };
                    }
                }
                else if ((slowMovingAverageCurr > fastMovingAverageCurr) && (MathAbs(slowMovingAverageCurr - fastMovingAverageCurr) > 0.00005))
                {
                    // xxx sell in case rsi is falling
                    if (iRSI(tradingSymbol.Name, PERIOD_M15, 14, PRICE_CLOSE, 0) < 40)
                    {
                        entryPrice = iLow(tradingSymbol.Name, PERIOD_M15, 1) - 1 * point;
                        order = new Order()
                        {
                            SymbolName = tradingSymbol.Name,
                            Operation = OrderOperation.SellStop,
                            OriginalLots = _settings.LotSize,
                            Lots = _settings.LotSize,
                            EntryPrice = entryPrice,
                            StopLoss = NormalizeDouble(entryPrice + _settings.StopLoss * point, digits),
                            TakeProfit = NormalizeDouble(entryPrice - _settings.TakeProfit * point, digits),
                            Color = Color.Red
                        };
                    }
                }

                return order != null;
            }
            catch (Exception ex)
            {
                NQLog.Error($"Create orders error : {ex.Message}.");
                return false;
            }
        }

        void PlaceOrder(Order order)
        {
            try
            {
                if (order == null) return;

                // we're trying to trigger multiple order send commands until one gets activated by MT4 platform.
                int attempts = 1;
                while (true)
                {
                    int ticketId = OrderSend(order.SymbolName, (int)order.Operation, order.Lots, order.EntryPrice, 3, order.StopLoss,
                                                order.TakeProfit, order.Comment, order.MagicNumber, order.ActiveTo, order.Color);

                    if (!ErrorOccurred() && ticketId != -1)
                    {
                        order.TicketId = ticketId;
   
                        _placedOrders.Add(order);

                        NQLog.Info($"({order.SymbolName}) Order successfully placed with ticket '{order.TicketId}'.");
                        break;
                    }
                    else if (attempts >= PLACE_ORDER_ATTEMPTS)
                    {
                        NQLog.Debug($"({order.SymbolName}) Place order attempt limit of {PLACE_ORDER_ATTEMPTS} attempts reached. Can't place order -- (try to fix the issue or set PLACE_ORDER_ATTEMPTS to higher value).");
                        break;
                    }
                    else
                    {
                        NQLog.Warn($"({order.SymbolName}) Failed to place order. Trying again...");
                        attempts++;
                    }
                }
            }
            catch (Exception ex)
            {
                NQLog.Error($"Place orders error : {ex.Message}.");
            }
        }

        bool ErrorOccurred()
        {
            int errorCode = GetLastError();

            if (errorCode > 1)
            {
                NQLog.Error($"!!! Error code '{errorCode}' occurred. !!!");

                ResetLastError();
                return true;
            }

            return false;
        }

        bool GetPlacedOrderData(Order order, out int placedOrderIndex, out KeyValuePair<double, double> closureRate)
        {
            placedOrderIndex = -1;
            closureRate = default;

            if (order == null)
            {
                NQLog.Warn($"Order does not exist.");
                return false;
            }

            if (order.ProfitPips <= 0)
            {
                //NQLog.Warn($"Order is not in profit. Order ticket ID: {order.TicketId}");
                return false;
            }

            placedOrderIndex = _placedOrders.FindIndex(o => o.TicketId == order.TicketId);

            if (placedOrderIndex == -1)
            {
                NQLog.Warn($"({order.SymbolName}) Failed to get placed order index. Weird situation (partial order close issue?), invalid argument order ticket ID {order.TicketId} ???");
                return false;
            }

            foreach (var kvp in _settings.ClosureRates)
            {
                if (!_placedOrders[placedOrderIndex].ClosureRates.TryGetValue(kvp.Key, out double value) && order.ProfitPips >= kvp.Key)
                {
                    NQLog.Debug($"({order.SymbolName}) Order profit pips {order.ProfitPips} reached order close profit pips {kvp.Key}. Using order close percentage: {kvp.Value}.");
                    closureRate = new KeyValuePair<double, double>(kvp.Key, kvp.Value);
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Obtain new ticket Id when old order gets closed and new order is placed by partial order close.
        /// </summary>
        /// <param name="order"></param>
        /// <param name="newTicketId"></param>
        /// <returns></returns>
        bool GetNewTicketId(Order order, out int newTicketId)
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

                if (orderTicketId > order.TicketId && OrderOpenTime() == order.OpenTime)
                {
                    newTicketId = orderTicketId;
                    return true;
                }
            }

            return false;
        }

        List<Order> GetActiveOrders()
        {
            List<Order> activeOrders = new List<Order>();

            for (int i = 0; i < OrdersTotal(); i++)
            {
                if (!OrderSelect(i, SELECT_BY_POS, MODE_TRADES)) continue;

                Order activeOrder = new Order()
                {
                    TicketId = OrderTicket(),
                    SymbolName = OrderSymbol(),
                    Operation = (OrderOperation)OrderType(),
                    TakeProfit = OrderTakeProfit(),
                    StopLoss = OrderStopLoss(),
                    EntryPrice = OrderOpenPrice(),
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

        void Dump<T>(IEnumerable<T> data)
        {
            foreach (var d in data)
            {
                NQLog.Info(d.ToString());
            }
        }
    }
}


