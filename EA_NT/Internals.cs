using EA.Data;
using NQuotes;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;

namespace EA
{
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

        bool ValidTradingTime(TradingSymbol tradingSymbol)
        {
            if (tradingSymbol == null)
                return false;

            DateTime currentTime = TimeCurrent();
            //if (currentTime.DayOfWeek == System.DayOfWeek.Friday)
            //{
            //    //xxx check if we really should skip fridays for trading ???
            //    return false;
            //}

            foreach (TimeInterval ti in tradingSymbol.TimeIntervals)
            {
                if (currentTime.Hour >= ti.FromHour && currentTime.Hour <= ti.ToHour)
                {
                    if (currentTime.Hour == ti.FromHour && currentTime.Minute < ti.FromMinute)
                        return false;
                    else if (currentTime.Hour == ti.ToHour && currentTime.Minute > ti.ToMinute)
                        return false;
                    return true;
                }
            }

            return false;
        }

        bool ValidCurrentPrice(TradingSymbol tradingSymbol)
        {
            if (tradingSymbol == null) return false;

            if (MarketInfo(tradingSymbol.Name, MODE_BID) > iHigh(tradingSymbol.Name, PERIOD_M30, 1))
            {
                NQLog.Warn($"({tradingSymbol.Name}) BUY price higher than previous candle high. Waiting for new bar.");
                tradingSymbol.WaitNewBar = true;
                return false;
            }
            else if (MarketInfo(tradingSymbol.Name, MODE_BID) < iLow(tradingSymbol.Name, PERIOD_M30, 1))
            {
                NQLog.Warn($"({tradingSymbol.Name}) SELL price lower than previous candle low. Waiting for new bar.");
                tradingSymbol.WaitNewBar = true;
                return false;
            }

            return true;
        }

        bool CreateOrders(TradingSymbol tradingSymbol, out List<Order> orders, bool createVirtualOrders = false)
        {
            orders = new List<Order>();

            try
            {
                double entryPrice = 0;
                double point = MarketInfo(tradingSymbol.Name, MODE_POINT);
                int digits = (int)MarketInfo(tradingSymbol.Name, MODE_DIGITS);
                List<Order> tradingSymbolOrders = new List<Order>();

                if (!createVirtualOrders)
                {
                    DateTime fromCurrentHour = new DateTime(TimeCurrent().Year, TimeCurrent().Month, TimeCurrent().Day, TimeCurrent().Hour, 0, 0);
                    tradingSymbolOrders = GetActiveOrders().Concat(GetHistoryOrders(fromCurrentHour)).Where(o => o.SymbolName == tradingSymbol.Name).ToList();
                }

                if (iOpen(tradingSymbol.Name, PERIOD_M30, 0) > iOpen(tradingSymbol.Name, PERIOD_M30, 1))
                {
                    if (createVirtualOrders || tradingSymbolOrders.FirstOrDefault(o => o.Operation == OrderOperation.BuyStop || o.Operation == OrderOperation.Buy) == null)
                    {
                        entryPrice = iHigh(tradingSymbol.Name, PERIOD_M30, 1) + 1 * point;
                        orders.Add(new Order()
                        {
                            SymbolName = tradingSymbol.Name,
                            Operation = OrderOperation.BuyStop,
                            OriginalLots = _settings.LotSize,
                            Lots = _settings.LotSize,
                            EntryPrice = entryPrice,
                            StopLoss = NormalizeDouble(entryPrice - _settings.StopLoss * point, digits),
                            TakeProfit = NormalizeDouble(entryPrice + _settings.TakeProfit * point, digits),
                            Color = Color.Green
                        });
                    }

                    //if (ValidTradingTime(tradingSymbol) &&
                    //   (createVirtualOrders || tradingSymbolOrders.FirstOrDefault(o => o.Operation == OrderOperation.SellStop || o.Operation == OrderOperation.Sell) == null))
                    //{
                    //    entryPrice = iLow(tradingSymbol.Name, PERIOD_M30, 1) - 1 * point;
                    //    orders.Add(new Order()
                    //    {
                    //        SymbolName = tradingSymbol.Name,
                    //        Operation = OrderOperation.SellStop,
                    //        OriginalLots = _settings.LotSize,
                    //        Lots = _settings.LotSize,
                    //        EntryPrice = entryPrice,
                    //        StopLoss = NormalizeDouble(entryPrice + _settings.StopLoss * point, digits),
                    //        TakeProfit = NormalizeDouble(entryPrice - _settings.TakeProfit * point, digits),
                    //        Color = Color.Red
                    //    });
                    //}
                }
                else if (iOpen(tradingSymbol.Name, PERIOD_M30, 0) < iOpen(tradingSymbol.Name, PERIOD_M30, 1))
                {
                    if (createVirtualOrders || tradingSymbolOrders.FirstOrDefault(o => o.Operation == OrderOperation.SellStop || o.Operation == OrderOperation.Sell) == null)
                    {
                        entryPrice = iLow(tradingSymbol.Name, PERIOD_M30, 1) - 1 * point;
                        orders.Add(new Order()
                        {
                            SymbolName = tradingSymbol.Name,
                            Operation = OrderOperation.SellStop,
                            OriginalLots = _settings.LotSize,
                            Lots = _settings.LotSize,
                            EntryPrice = entryPrice,
                            StopLoss = NormalizeDouble(entryPrice + _settings.StopLoss * point, digits),
                            TakeProfit = NormalizeDouble(entryPrice - _settings.TakeProfit * point, digits),
                            Color = Color.Red
                        });
                    }

                    //if (ValidTradingTime(tradingSymbol) &&
                    //   (createVirtualOrders || tradingSymbolOrders.FirstOrDefault(o => o.Operation == OrderOperation.BuyStop || o.Operation == OrderOperation.Buy) == null))
                    //{
                    //    entryPrice = iHigh(tradingSymbol.Name, PERIOD_M30, 1) + 1 * point;
                    //    orders.Add(new Order()
                    //    {
                    //        SymbolName = tradingSymbol.Name,
                    //        Operation = OrderOperation.BuyStop,
                    //        OriginalLots = _settings.LotSize,
                    //        Lots = _settings.LotSize,
                    //        EntryPrice = entryPrice,
                    //        StopLoss = NormalizeDouble(entryPrice - _settings.StopLoss * point, digits),
                    //        TakeProfit = NormalizeDouble(entryPrice + _settings.TakeProfit * point, digits),
                    //        Color = Color.Green
                    //    });
                    //}
                }

                return orders.Count > 0;
            }
            catch (Exception ex)
            {
                NQLog.Error($"Create orders error : {ex.Message}.");
                return false;
            }
        }

        void PlaceOrders(List<Order> orders)
        {
            try
            {
                if (orders == null || orders.Count == 0) return;

                foreach (Order order in orders)
                {
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
            }
            catch (Exception ex)
            {
                NQLog.Error($"Place orders error : {ex.Message}.");
            }
        }

        bool DeleteOrder(TradingSymbol tradingSymbol)
        {
            if (tradingSymbol == null) return false;

            List<Order> pendingOrders = GetActiveOrders().Where(o => o.SymbolName == tradingSymbol.Name && o.Operation != OrderOperation.Buy || o.Operation != OrderOperation.Sell).ToList();

            foreach (Order pendingOrder in pendingOrders)
            {
                OrderDelete(pendingOrder.TicketId);

                if (ErrorOccurred())
                {
                    NQLog.Warn($"({pendingOrder.SymbolName}) Order ticket {pendingOrder.TicketId} failed to delete.");
                    continue;
                }

                NQLog.Info($"({pendingOrder.SymbolName}) Order ticket {pendingOrder.TicketId} successfully deleted.");
            }

            return true;
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

        bool CheckForNewBar(TradingSymbol tradingSymbol)
        {
            if (tradingSymbol == null) 
                return false;

            int bars = iBars(tradingSymbol.Name, PERIOD_M30);

            if (tradingSymbol.Bars == bars) 
                return false; // no new bars, skip.

            if (tradingSymbol.Bars == 0)
            {
                tradingSymbol.Bars = bars;
                return false; // skip for the first time on start.
            }

            NQLog.Info($"New bar available ({tradingSymbol}).");

            tradingSymbol.WaitNewBar = false;
            tradingSymbol.Bars = bars;
            return true;
        }

        bool ActiveMarketOrder(TradingSymbol tradingSymbol)
        {
            if (tradingSymbol == null) 
                return false;

            return GetActiveOrders().Where(o => o.SymbolName == tradingSymbol.Name && (o.Operation == OrderOperation.Buy || o.Operation == OrderOperation.Sell)).Any();
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
                NQLog.Warn($"Order is not in profit. Order ticket ID: {order.TicketId}");
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

        void ModifyMarketOrders()
        {
            List<Order> marketOrders = GetActiveOrders().Where(o => o.Operation == OrderOperation.Buy || o.Operation == OrderOperation.Sell).ToList();

            foreach (Order marketOrder in marketOrders)
            {
                double bid = MarketInfo(marketOrder.SymbolName, MODE_BID);
                double point = MarketInfo(marketOrder.SymbolName, MODE_POINT);
                double highPrice = iHigh(marketOrder.SymbolName, PERIOD_M30, 1);
                double lowPrice = iLow(marketOrder.SymbolName, PERIOD_M30, 1);
                int digits = (int)MarketInfo(marketOrder.SymbolName, MODE_DIGITS);
                bool orderModify = false;

                if (marketOrder.Operation == OrderOperation.Buy)
                {
                    if (marketOrder.StopLoss < marketOrder.EntryPrice && bid > NormalizeDouble(marketOrder.EntryPrice + _settings.BreakEven * point , digits))
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

        //void ClosePositiveMarketOrder(TradingSymbol tradingSymbol)
        //{
        //    if (tradingSymbol == null) return;

        //    List<Order> marketOrders = GetActiveOrders().Where(o => o.SymbolName == tradingSymbol.Name && o.Operation == OrderOperation.Buy || o.Operation == OrderOperation.Sell).ToList();

        //    foreach (Order marketOrder in marketOrders)
        //    {
        //        // check if order exists so far.
        //        if (!OrderSelect(marketOrder.TicketId, SELECT_BY_TICKET))
        //        {
        //            NQLog.Warn($"({marketOrder.SymbolName}) Order ticket {marketOrder.TicketId} seems already closed.");
        //            continue;
        //        }

        //        if (OrderProfit() > 0)
        //        {
        //            OrderClose(marketOrder.TicketId, marketOrder.Lots, OrderClosePrice(), 0);

        //            if (ErrorOccurred())
        //            {
        //                NQLog.Warn($"({marketOrder.SymbolName}) Order ticket {marketOrder.TicketId} failed to close.");
        //                continue;
        //            }

        //            NQLog.Info($"({marketOrder.SymbolName}) Order ticket {marketOrder.TicketId} successfully closed.");
        //        }
        //    }
        //}

        void PlaceVirtualOrders(TradingSymbol tradingSymbol, List<Order> virtualOrders)
        {
            if (tradingSymbol == null || virtualOrders == null || virtualOrders.Count == 0) return;

            List<Order> orders = new List<Order>();

            // extract Buy pending order for trading symbol.
            Order buyPendingOrder = GetActiveOrders().FirstOrDefault(o => o.SymbolName == tradingSymbol.Name && o.Operation == OrderOperation.BuyStop);
            Order buyVirtualOrder = virtualOrders.FirstOrDefault(o => o.SymbolName == tradingSymbol.Name && o.Operation == OrderOperation.BuyStop);

            if (buyVirtualOrder?.EntryPrice < buyPendingOrder?.EntryPrice)
            {
                NQLog.Debug("BUY virtual entry price < BUY pending entry price -> Deleting order and placing BUY virtual order.");
                if (DeleteOrder(tradingSymbol)) orders.Add(buyVirtualOrder);
            }
            else if (buyPendingOrder == null && buyVirtualOrder != null)
            {
                NQLog.Debug("NO BUY pending order, placing BUY virtual order.");
                orders.Add(buyVirtualOrder);
            }

            // extract Sell pending order for trading symbol.
            Order sellPendingOrder = GetActiveOrders().FirstOrDefault(o => o.SymbolName == tradingSymbol.Name && o.Operation == OrderOperation.SellStop);
            Order sellVirtualOrder = virtualOrders.FirstOrDefault(o => o.SymbolName == tradingSymbol.Name && o.Operation == OrderOperation.SellStop);

            if (sellVirtualOrder?.EntryPrice > sellPendingOrder?.EntryPrice)
            {
                NQLog.Debug("SELL virtual entry price > SELL pending entry price -> Deleting order and placing SELL virtual order.");
                if (DeleteOrder(tradingSymbol)) orders.Add(sellVirtualOrder);
            }
            else if (sellPendingOrder == null && sellVirtualOrder != null)
            {
                NQLog.Debug("NO SELL pending order, placing SELL virtual order.");
                orders.Add(sellVirtualOrder);
            }

            PlaceOrders(orders);
        }

        void OpenMarketOrders()
        {
            foreach (TradingSymbol tradingSymbol in _tradingSymbols)
            {
                if (!ValidTradingTime(tradingSymbol) || ActiveMarketOrder(tradingSymbol))
                    continue;

                bool newBar = CheckForNewBar(tradingSymbol);

                if (tradingSymbol.WaitNewBar || !ValidCurrentPrice(tradingSymbol))
                    continue;

                if (CreateOrders(tradingSymbol, out List<Order> orders))
                    PlaceOrders(orders);

                if (newBar && CreateOrders(tradingSymbol, out List<Order> virtualOrders, createVirtualOrders: true))
                    PlaceVirtualOrders(tradingSymbol, virtualOrders);
            }
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

        List<Order> GetHistoryOrders(DateTime? fromTime)
        {
            List<Order> historyOrders = new List<Order>();

            for (int i = 0; i < OrdersHistoryTotal(); i++)
            {
                if (!OrderSelect(i, SELECT_BY_POS, MODE_HISTORY) || (fromTime.HasValue && OrderCloseTime() < fromTime)) 
                    continue;

                Order historyOrder = new Order()
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

                historyOrders.Add(historyOrder);
            }

            return historyOrders;
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


