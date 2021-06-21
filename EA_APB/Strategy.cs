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
        private Settings _settings;
        private List<APBCandle> _apbCandles;
        private Order _currentOrder;
        private DateTime _lostOrderTime;
        private bool _reversalAPBCandleDetected;
        private int _currentCandles;
        private int _placeOrderAttempts;

        #region Core MQL API methods

        public override int init()
        {
            _settings = new Settings();
            _apbCandles = new List<APBCandle>(20);
            _currentOrder = null;
            _lostOrderTime = TimeCurrent();
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
                NQLog.Info($"New candle is available.");
                CalculateAPBCandles();
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
                if (!BeginOrder()) return false;

                if (!CreateOrder(out Order order)) 
                    return false;

                NQLog.Info($"Created order '{JsonConvert.SerializeObject(order)}'.");

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

                        NQLog.Info($"Order successfully placed with ticket '{_currentOrder.TicketId}'.");
                        return true;
                    }
                    else if (attempts >= _placeOrderAttempts)
                    {
                        NQLog.Debug($"Place order attempt limit of {_placeOrderAttempts} attempts reached. Can't place order -- (try to fix the issue or set _placeOrderAttempts to higher value).");
                        return false;
                    }

                    NQLog.Warn($"Failed to place order. Trying again...");
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

                // we need to perform order select MT4 command because this 4 values changes regulary and can't be stored internally in _currentOrder.
                OrderOperation orderOperation = (OrderOperation)OrderType();
                double stopLoss = OrderStopLoss();
                double takeProfit = OrderTakeProfit();
                double price = OrderOpenPrice();
                APBCandle firstAPBCandle = _apbCandles.First();

                if (newCandle && orderOperation >= OrderOperation.BUY_LIMIT && orderOperation <= OrderOperation.SELL_STOP) // modify pending order only when new candle is available.
                {
                    if (orderOperation == OrderOperation.BUY_STOP || orderOperation == OrderOperation.BUY_LIMIT)
                    {
                        NQLog.Debug($"Pending BUY; Setting price ABOVE 1st APB candle.");

                        price = NormalizeDouble(firstAPBCandle.High + _settings.ExtremeDiff * Point, 5);
                        stopLoss = NormalizeDouble(price - _settings.StopLoss * Point, 5);
                        takeProfit = NormalizeDouble(price + _settings.TakeProfit * Point, 5);
                        _currentOrder.PendingModify = true;
                    }
                    else if (orderOperation == OrderOperation.SELL_STOP || orderOperation == OrderOperation.SELL_LIMIT)
                    {
                        NQLog.Debug($"Pending SELL; Setting price BELOW 1st APB candle.");

                        price = NormalizeDouble(firstAPBCandle.Low - _settings.ExtremeDiff * Point, 5);
                        stopLoss = NormalizeDouble(price + _settings.StopLoss * Point, 5);
                        takeProfit = NormalizeDouble(price - _settings.TakeProfit * Point, 5);
                        _currentOrder.PendingModify = true;
                    }
                }
                else if (orderOperation == OrderOperation.BUY) // try to modify BUY market order on every MT4 platform signal.
                {
                    if (stopLoss < price && Bid > NormalizeDouble(price + _settings.BreakEven * Point, 5)) // 1. set BE point in case BE point not set yet.
                    {
                        NQLog.Debug($"BUY; BE point reached. Setting stoploss with price {price}.");
                        stopLoss = price;

                        _currentOrder.BreakEvenReached = true;
                        _currentOrder.PendingModify = true;
                    }
                    else if (_reversalAPBCandleDetected && firstAPBCandle.Color == CandleColor.SELL) // 2. wait for first reversal candle.
                    {
                        double reversalLowPrice = NormalizeDouble(firstAPBCandle.Low - _settings.ExtremeDiff * Point, 5);

                        if (reversalLowPrice > stopLoss) // 3. check if first APB candle price (low with diff of reversal candle) is bigger than stoploss in that case move stoploss closer to takeprofit.
                        {
                            NQLog.Debug($"BUY; Setting stoploss with reversal LOW price {reversalLowPrice}.");
                            stopLoss = reversalLowPrice;
                            _currentOrder.PendingModify = true;
                        }
                    }
                }
                else if (orderOperation == OrderOperation.SELL) // try to modify SELL market order on every MT4 platform signal.
                {
                    if (stopLoss > price && Bid < NormalizeDouble(price - _settings.BreakEven * Point, 5))
                    {
                        NQLog.Debug($"SELL; BE point reached. Setting stoploss with price {price}.");
                        stopLoss = price;

                        _currentOrder.BreakEvenReached = true;
                        _currentOrder.PendingModify = true;
                    }
                    else if (_reversalAPBCandleDetected && firstAPBCandle.Color == CandleColor.BUY)
                    {
                        double reversalHighPrice = NormalizeDouble(firstAPBCandle.High + _settings.ExtremeDiff * Point, 5);

                        if (reversalHighPrice < stopLoss)
                        {
                            NQLog.Debug($"SELL; Setting stoploss with reversal HIGH price {reversalHighPrice}.");
                            stopLoss = reversalHighPrice;
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
                        NQLog.Warn($"Order ticket {_currentOrder.TicketId} failed to modify.");
                        return false;
                    }

                    _currentOrder.PendingModify = false;
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

                _reversalAPBCandleDetected = _apbCandles[0].Color != _apbCandles[1].Color;

                NQLog.Info($"Reversal APB candle detected: '{_reversalAPBCandleDetected}'. First closed APB candle color: {_apbCandles[0].Color}.");
            }
            catch (Exception ex)
            {
                NQLog.Error($"Calculate APB candles error : {ex.Message}.");
            }
        }

        private bool CreateOrder(out Order order)
        {
            order = null;

            try
            {
                OrderOperation orderOperation = GetOrderOperation();

                if (orderOperation >= OrderOperation.BUY_LIMIT && orderOperation <= OrderOperation.SELL_STOP)
                {
                    if (orderOperation == OrderOperation.BUY_STOP)
                    {
                        double buyPrice = NormalizeDouble(_apbCandles.First().High + _settings.ExtremeDiff * Point, 5);

                        order = new Order()
                        {
                            Symbol = Symbol(),
                            Operation = orderOperation,
                            Lots = _settings.LotSize,
                            EntryPrice = buyPrice,
                            StopLoss = NormalizeDouble(buyPrice - _settings.StopLoss * Point, 5),
                            TakeProfit = NormalizeDouble(buyPrice + _settings.TakeProfit * Point, 5),
                            Color = Color.Green
                        };
                    }
                    else if (orderOperation == OrderOperation.SELL_STOP)
                    {
                        double sellPrice = NormalizeDouble(_apbCandles.First().Low - _settings.ExtremeDiff * Point, 5);

                        order = new Order()
                        {
                            Symbol = Symbol(),
                            Operation = orderOperation,
                            Lots = _settings.LotSize,
                            EntryPrice = sellPrice,
                            StopLoss = NormalizeDouble(sellPrice + _settings.StopLoss * Point, 5),
                            TakeProfit = NormalizeDouble(sellPrice - _settings.TakeProfit * Point, 5),
                            Color = Color.Red
                        };
                    }
                }
                else if (orderOperation == OrderOperation.BUY)
                {
                    order = new Order()
                    {
                        Symbol = Symbol(),
                        Operation = orderOperation,
                        Lots = _settings.LotSize,
                        EntryPrice = Ask,
                        StopLoss = NormalizeDouble(Ask - _settings.StopLoss * Point, 5),
                        TakeProfit = NormalizeDouble(Ask + _settings.TakeProfit * Point, 5),
                        Color = Color.DarkGreen
                    };
                }
                else if (orderOperation == OrderOperation.SELL)
                {
                    order = new Order()
                    {
                        Symbol = Symbol(),
                        Operation = orderOperation,
                        Lots = _settings.LotSize,
                        EntryPrice = Bid,
                        StopLoss = NormalizeDouble(Bid + _settings.StopLoss * Point, 5),
                        TakeProfit = NormalizeDouble(Bid - _settings.TakeProfit * Point, 5),
                        Color = Color.DarkRed
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

                if (OrderProfit() < 0 && OrderCloseTime() > _lostOrderTime)
                {
                    _lostOrderTime = OrderCloseTime();

                    if (OrderClosePrice() == OrderStopLoss())
                    {
                        NQLog.Debug($"BE order with ticket '{OrderTicket()}' was closed in loss. Skipping it...");
                        continue;
                    }

                    order = new Order()
                    {
                        TicketId = OrderTicket(),
                        Symbol = OrderSymbol(),
                        Operation = (OrderOperation)OrderType(),
                        Lots = OrderLots(),
                        EntryPrice = OrderOpenPrice(),
                        StopLoss = OrderStopLoss(),
                        TakeProfit = OrderTakeProfit(),
                        Comment = OrderComment()
                    };

                    return true;
                }
            }

            return false;
        }

        private OrderOperation GetOrderOperation()
        {
            OrderOperation orderOperation = OrderOperation.NONE;

            if (OrderLossDetected(out Order order)) // 1. check if order is lost and open reversal MARKET order immediately.
            {
                NQLog.Info($"Order loss detected - open of reversal market order needed. Lost order data '{JsonConvert.SerializeObject(order)}'.");

                switch (order.Operation)
                {
                    case OrderOperation.BUY:
                        orderOperation = OrderOperation.SELL; break;
                    case OrderOperation.SELL:
                        orderOperation = OrderOperation.BUY; break;

                    default:
                        NQLog.Warn($"Unknown order operation. Can't create reversal market order operation.");
                        break;
                }

            }
            else if (_reversalAPBCandleDetected) // 2. in case reversal candle detected, open MARKET order in that direction.
            {
                switch (_apbCandles.First().Color)
                {
                    case CandleColor.BUY:
                        orderOperation = OrderOperation.BUY; break;
                    case CandleColor.SELL:
                        orderOperation = OrderOperation.SELL; break;

                    default:
                        NQLog.Warn($"Unknown candle color. Can't create market order operation.");
                        break;
                }
            }
            else // 3. otherwise look candle color to create PENDING market order
            {
                switch (_apbCandles.First().Color)
                {
                    case CandleColor.BUY:
                        orderOperation = OrderOperation.SELL_STOP; break;
                    case CandleColor.SELL:
                        orderOperation = OrderOperation.BUY_STOP; break;

                    default:
                        NQLog.Warn($"Unknown candle color. Can't create pending order operation.");
                        break;
                }
            }

            return orderOperation;
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
            int currentHour = TimeCurrent().Hour;

            return !(currentHour < _settings.OpenHour || currentHour >= _settings.CloseHour);
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
                NQLog.Error($"!!! Error code '{errorCode}' occurred. !!!");

                ResetLastError();
                return true;
            }

            return false;
        }

        #endregion
    }
}