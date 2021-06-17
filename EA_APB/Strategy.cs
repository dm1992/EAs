using EA.Data;
using Newtonsoft.Json;
using NQuotes;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace EA
{
    public class Strategy : MqlApi
    {
        private Order _currentOrder;
        private List<APBCandle> _apbCandles;
        private Settings _settings;
        private DateTime _lostOrderTime;
        private bool _initialAPBCandlesCalculation;
        private bool _reversalAPBCandleDetected;
        private int _currentCandles;
        private int _placeOrderAttempts;

        #region Core MQL API methods

        public override int init()
        {
            _currentOrder = null;
            _apbCandles = new List<APBCandle>(20);
            _settings = new Settings();
            _lostOrderTime = DateTime.Now.AddHours(1); // broker time
            _initialAPBCandlesCalculation = false;
            _reversalAPBCandleDetected = false;
            _currentCandles = 0;
            _placeOrderAttempts = 20;
            
            return 0;
        }

        public override int deinit()
        {
            return 0;
        }

        public override int start()
        {
            RefreshRates();

            bool newCandle = NewCandleAvailable();

            if (newCandle)
            {
                NQLog.Info($"New candle available.");

                CalculateAPBCandles();

                if (_initialAPBCandlesCalculation)
                {
                    _reversalAPBCandleDetected = _apbCandles[0].Color != _apbCandles[1].Color;
                    NQLog.Info($"Reversal APB candle detected: '{_reversalAPBCandleDetected}'.");
                }
                else
                {
                    NQLog.Info($"Initial APB candles calculation performed.");
                    _initialAPBCandlesCalculation = true;
                }
            }

            PlaceOrder();
            ModifyOrder(newCandle);
            return 0;
        }

        #endregion

        #region Strategy trading methods

        private bool PlaceOrder()
        {
            try
            {
                OrderOperation? orderOperation = null;

                if (!BeginOrder()) return false;

                if (OrderLossDetected(out Order order))
                {
                    NQLog.Info($"Order loss detected - open of reversal order needed. Lost order data '{JsonConvert.SerializeObject(order)}'.");
                    orderOperation = order.Operation;
                }

                if (!CreateOrder(orderOperation, out order))
                    return false;

                // we're trying to trigger multiple order send commands until one gets activated by MT4 platform.
                int attempts = 1;
                while (true)
                {
                    int ticketId = OrderSend(order.Symbol, (int)order.Operation, order.Lots, order.EntryPrice, 3, order.StopLoss, 
                                          order.TakeProfit, order.Comment, order.MagicNumber, order.ActiveTo, order.Color);

                    if (!ErrorOccurred() && ticketId != -1)
                    {
                        _currentOrder = order;
                        _currentOrder.TicketId = ticketId;

                        NQLog.Info($"Placed order '{JsonConvert.SerializeObject(_currentOrder)}'.");
                        return true;
                    }
                    else if (attempts >= _placeOrderAttempts)
                    {
                        NQLog.Debug($"Place order attempt limit of {_placeOrderAttempts} attempts reached. Can't place order -- (try to fix the issue or set _placeOrderAttempts to higher value).");
                        return false;
                    }

                    NQLog.Warn($"Failed to send order. Trying to place order again...");
                    attempts++;
                }
            }
            catch (Exception ex)
            {
                NQLog.Error($"Place order error : {ex.Message}.");
                return false;
            }
        }

        private bool ModifyOrder(bool newCandle)
        {
            try
            {
                if (_currentOrder == null) return false;

                if (!IsOrderActive() || !OrderSelect(_currentOrder.TicketId, SELECT_BY_TICKET))
                    return false;

                APBCandle firstAPBCandle = _apbCandles.First();
                double firstAPBCandlePrice = 0.0;

                // we need to perform order select MT4 command because this 4 values changes regulary and can't be stored internally in _currentOrder.
                OrderOperation operation = (OrderOperation)OrderType();
                double stopLoss = OrderStopLoss();
                double takeProfit = OrderTakeProfit();
                double price = OrderOpenPrice();

                if (newCandle && operation >= OrderOperation.BUY_LIMIT && operation <= OrderOperation.SELL_STOP) // modify pending order only when new candle is available.
                {
                    if (operation == OrderOperation.BUY_STOP || operation == OrderOperation.BUY_LIMIT)
                    {
                        NQLog.Debug($"Pending BUY; Setting price ABOVE 1st APB candle.");

                        price = firstAPBCandle.High + _settings.ExtremeDiff * Point;
                        stopLoss = NormalizeDouble(price - _settings.StopLoss * Point, 5);
                        takeProfit = NormalizeDouble(price + _settings.TakeProfit * Point, 5);
                        _currentOrder.PendingModify = true;
                    }
                    else if (operation == OrderOperation.SELL_STOP || operation == OrderOperation.SELL_LIMIT)
                    {
                        NQLog.Debug($"Pending SELL; Setting price BELOW 1st APB candle.");

                        price = firstAPBCandle.Low - _settings.ExtremeDiff * Point;
                        stopLoss = NormalizeDouble(price + _settings.StopLoss * Point, 5);
                        takeProfit = NormalizeDouble(price - _settings.TakeProfit * Point, 5);
                        _currentOrder.PendingModify = true;
                    }
                }
                else if (operation == OrderOperation.BUY) // try to modify BUY market order on every MT4 platform signal.
                {
                    if (!_currentOrder.BreakEvenReached && Bid > NormalizeDouble(price + _settings.BreakEven * Point, 5)) // 1. set BE point in case BE point not set yet.
                    {
                        NQLog.Debug($"BUY; BE point reached. Setting stoploss with price {price}.");
                        stopLoss = price;

                        _currentOrder.BreakEvenReached = true;
                        _currentOrder.PendingModify = true;
                    }   
                    
                    if (_reversalAPBCandleDetected) // 2. wait for first reversal candle.
                    {
                        firstAPBCandlePrice = NormalizeDouble(firstAPBCandle.Low - _settings.ExtremeDiff * Point, 5);

                        if (firstAPBCandlePrice > stopLoss) // 3. check if first APB candle price (low with diff of reversal candle) is bigger than stoploss in that case move stoploss closer to takeprofit.
                        {
                            NQLog.Debug($"BUY; Setting stoploss with reversal APB candle LOW price {firstAPBCandlePrice}.");
                            stopLoss = firstAPBCandlePrice;
                            _currentOrder.PendingModify = true;
                        }
                    }
                }
                else if (operation == OrderOperation.SELL) // try to modify SELL market order on every MT4 platform signal.
                {
                    if (!_currentOrder.BreakEvenReached && Ask < NormalizeDouble(price - _settings.BreakEven * Point, 5))
                    {
                        NQLog.Debug($"SELL; BE point reached. Setting stoploss with price {price}.");
                        stopLoss = price;

                        _currentOrder.BreakEvenReached = true;
                        _currentOrder.PendingModify = true;
                    }

                    if (_reversalAPBCandleDetected)
                    {
                        firstAPBCandlePrice = NormalizeDouble(firstAPBCandle.High + _settings.ExtremeDiff * Point, 5);

                        if (firstAPBCandlePrice < stopLoss)
                        {
                            NQLog.Debug($"SELL; Setting stoploss with reversal APB candle HIGH price {firstAPBCandlePrice}.");
                            stopLoss = firstAPBCandlePrice;
                            _currentOrder.PendingModify = true;
                        }
                    }
                }
                
                if (_currentOrder.PendingModify)
                {
                    // check if order exists so far.
                    if (!OrderSelect(_currentOrder.TicketId, SELECT_BY_TICKET))
                    {
                        NQLog.Warn($"Order ticket {_currentOrder.TicketId} seems closed. Can't modify order anymore.");
                        return false;
                    }

                    OrderModify(_currentOrder.TicketId, price, stopLoss, takeProfit, OrderExpiration());

                    if (ErrorOccurred())
                    {
                        NQLog.Warn($"Order ticket {_currentOrder.TicketId} failed to modify due to error '{ErrorDescription(GetLastError())}'.");
                        return false;
                    }

                    return true; // order modified
                } 
            }
            catch (Exception ex)
            {
                NQLog.Error($"Modify order error : {ex.Message}.");
            }

            return false;
        }

        #endregion

        #region Strategy helper methods

        private bool NewCandleAvailable()
        {
            int currentCandles = iBars(Symbol(), _settings.Period);

            if (_currentCandles == currentCandles)
                return false; // nothing new here

            _currentCandles = currentCandles;
            return true;
        }

        private void CalculateAPBCandles()
        {
            try
            {
                NQLog.Info($"APB candles calculation in progress.");

                if (_apbCandles.Count > 0) _apbCandles.Clear();

                APBCandle previousAPBCandle = null;
                for (int i = _apbCandles.Capacity; i > 0; i--)
                {
                    if (previousAPBCandle == null) 
                    {
                        // 21-th APB candle calculation, only to start calculation
                        previousAPBCandle = new APBCandle();
                        previousAPBCandle.Open = Math.Round((iOpen(Symbol(), _settings.Period, i + 1) + iClose(Symbol(), _settings.Period, i + 1)) / 2, 5);
                        previousAPBCandle.Close = Math.Round(((iOpen(Symbol(), _settings.Period, i + 1) + iHigh(Symbol(), _settings.Period, i + 1) + iLow(Symbol(), _settings.Period, i + 1) +
                                                iClose(Symbol(), _settings.Period, i + 1)) / 4 + iClose(Symbol(), _settings.Period, i + 1)) / 2, 5);

                        NQLog.Debug($"Initial {i + 1}. APB candle calculated with values '{JsonConvert.SerializeObject(previousAPBCandle)}'.");
                    }

                    var newAPBCandle = new APBCandle();
                    newAPBCandle.Open = Math.Round((previousAPBCandle.Open + previousAPBCandle.Close) / 2, 5);
                    newAPBCandle.Close = Math.Round(((iOpen(Symbol(), _settings.Period, i) + iHigh(Symbol(), _settings.Period, i) + iLow(Symbol(), _settings.Period, i) + 
                                         iClose(Symbol(), _settings.Period, i)) / 4 + iClose(Symbol(), _settings.Period, i)) / 2, 5);                   
                    newAPBCandle.High = Math.Round(Helpers.FindMax(new List<double>() { iHigh(Symbol(), _settings.Period, i), newAPBCandle.Open, newAPBCandle.Close }), 5);
                    newAPBCandle.Low = Math.Round(Helpers.FindMin(new List<double>() { iLow(Symbol(), _settings.Period, i), newAPBCandle.Open, newAPBCandle.Close }), 5);     
                    newAPBCandle.Color = newAPBCandle.Open < newAPBCandle.Close ? CandleColor.BUY : CandleColor.SELL;

                    NQLog.Debug($"{i}. APB candle calculated with values '{JsonConvert.SerializeObject(newAPBCandle)}'.");

                    _apbCandles.Insert(0, newAPBCandle);
                    previousAPBCandle = newAPBCandle;
                }
            }
            catch (Exception ex)
            {
                NQLog.Error($"Calculate APB candles error : {ex.Message}.");
            }
        }

        private bool CreateOrder(OrderOperation? orderOperation, out Order order)
        {
            order = null;

            try
            {
                APBCandle firstCandle = _apbCandles.First();
                double entryPrice;

                if (orderOperation.HasValue) // create reversal order immediately
                {
                    if (orderOperation == OrderOperation.BUY)
                    {
                        entryPrice = Ask;

                        order = new Order()
                        {
                            Symbol = Symbol(),
                            Operation = OrderOperation.SELL,
                            Lots = _settings.LotSize,
                            EntryPrice = entryPrice,
                            StopLoss = NormalizeDouble(entryPrice + _settings.StopLoss * Point, 5),
                            TakeProfit = NormalizeDouble(entryPrice - _settings.TakeProfit * Point, 5),
                            Color = Color.Red
                        };
                    }
                    else if (orderOperation == OrderOperation.SELL)
                    {
                        entryPrice = Bid;

                        order = new Order()
                        {
                            Symbol = Symbol(),
                            Operation = OrderOperation.BUY,
                            Lots = _settings.LotSize,
                            EntryPrice = entryPrice,
                            StopLoss = NormalizeDouble(entryPrice - _settings.StopLoss * Point, 5),
                            TakeProfit = NormalizeDouble(entryPrice + _settings.TakeProfit * Point, 5),
                            Color = Color.Green
                        };
                    }
                }
                else if (firstCandle.Color == CandleColor.BUY)
                {
                    entryPrice = firstCandle.Low - _settings.ExtremeDiff * Point;

                    order = new Order()
                    {
                        Symbol = Symbol(),
                        Operation = OrderOperation.SELL_STOP,
                        Lots = _settings.LotSize,
                        EntryPrice = entryPrice,
                        StopLoss = NormalizeDouble(entryPrice + _settings.StopLoss * Point, 5),
                        TakeProfit = NormalizeDouble(entryPrice - _settings.TakeProfit * Point, 5),
                        Color = Color.Red
                    };
                }
                else if (firstCandle.Color == CandleColor.SELL)
                {
                    entryPrice = firstCandle.High + _settings.ExtremeDiff * Point;

                    order = new Order()
                    {
                        Symbol = Symbol(),
                        Operation = OrderOperation.BUY_STOP,
                        Lots = _settings.LotSize,
                        EntryPrice = entryPrice,
                        StopLoss = NormalizeDouble(entryPrice - _settings.StopLoss * Point, 5),
                        TakeProfit = NormalizeDouble(entryPrice + _settings.TakeProfit * Point, 5),
                        Color = Color.Green
                    };
                }

                return order != null;
            }
            catch (Exception ex)
            {
                NQLog.Error($"Create order error : {ex.Message}.");
                return false;
            }
        }

        private bool BeginOrder()
        {
            return !IsOrderActive() && !DailyTradingPerformed() &&
                   !IsMarketRanging() && ValidTradingTime() && ValidLastCandle();
        }

        private bool DailyTradingPerformed()
        {
            if (!_settings.LimitTradePerDay) return false;

            DateTime todayMidnight = new DateTime(DateTime.Now.Year, DateTime.Now.Month, DateTime.Now.Day, 0, 0, 0);

            for (int i = OrdersHistoryTotal() - 1; i >= 0; i--)
            {
                if (!OrderSelect(i, SELECT_BY_POS, MODE_HISTORY))
                    continue;

                if (OrderCloseTime() >= todayMidnight && OrderSymbol() == Symbol() && OrderProfit() >= 0)
                {
                    NQLog.Debug($"Found daily order closed in profit. Trade per day goal achived.");
                    return true;
                }
            }

            return false;
        }

        private bool OrderLossDetected(out Order order)
        {
            order = null;

            for (int i = OrdersHistoryTotal() - 1; i >= 0; i--)
            {
                if (!OrderSelect(i, SELECT_BY_POS, MODE_HISTORY))
                    continue;

                DateTime orderCloseTime = OrderCloseTime();

                if (OrderProfit() < 0 && orderCloseTime > _lostOrderTime)
                {
                    order = new Order()
                    {
                        Symbol = OrderSymbol(),
                        Operation = (OrderOperation)OrderType(),
                        Lots = OrderLots(),
                        EntryPrice = OrderOpenPrice(),
                        StopLoss = OrderStopLoss(),
                        TakeProfit = OrderTakeProfit(),
                        Comment = OrderComment()
                    };

                    _lostOrderTime = orderCloseTime;
                    return true;
                }
            }

            return false;
        }

        private bool IsOrderActive()
        {
            return OrdersTotal() > 0;
        }

        private bool IsMarketRanging()
        {
            if (_apbCandles.Count() < _settings.CandleSet)
            {
                NQLog.Warn($"Not enough APB candles for market ranging detection. Needed candles {_settings.CandleSet}. Available candles {_apbCandles.Count()}.");
                return false;
            }

            int colorChanges = 0;
            for (int i = 0; i < _settings.CandleSet; i++)
            {
                if (_apbCandles[i].Color != _apbCandles[i + 1].Color)
                {
                    colorChanges++;

                    if (colorChanges >= _settings.CandleColorChangesLimit)
                    {
                        NQLog.Debug($"Ranging market detected.");
                        return true;
                    }
                }
            }

            return false;
        }

        private bool ValidTradingTime()
        {
            DateTime currentTime = TimeCurrent();

            return !(currentTime.Hour < _settings.OpenHour || currentTime.Hour >= _settings.CloseHour);
        }

        private bool ValidLastCandle()
        {
            return (int)MathFloor(MathAbs(iHigh(Symbol(), _settings.Period, 1) - iLow(Symbol(), _settings.Period, 1)) / Point) < _settings.LastCandlePipSize;
        }

        public bool ErrorOccurred()
        {
            int errorCode = GetLastError();

            if (errorCode > 1)
            {
                ResetLastError();
                return true;
            }

            return false;
        }

        #endregion
    }
}