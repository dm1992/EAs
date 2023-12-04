using Bybit.Net;
using Bybit.Net.Clients;
using Bybit.Net.Enums;
using Bybit.Net.Objects.Models.Derivatives;
using Bybit.Net.Objects.Models.Socket.Derivatives;
using Common;
using CryptoExchange.Net.Objects;
using CryptoExchange.Net.Sockets;
using MarketClient.Interfaces.Socket;
using MarketProxy.Events;
using MarketProxy.Models;
using NLog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace MarketProxy.Socket.BybitSocket
{
    public class BybitTradeSocket : BybitBaseSocket, ITradeSocket
    {
        private readonly ILogger _logger;
        private readonly SemaphoreSlim _tradeReceiverSemaphore;
        private readonly SemaphoreSlim _orderbookUpdateSemaphore;
        private readonly SemaphoreSlim _orderbookSnapshotSemaphore;

        private Dictionary<string, BybitDerivativesTradeUpdate> _tradeReceiverBuffer;
        private Dictionary<string, BybitDerivativesOrderBookEntry> _orderbooks;

        public event EventHandler<OrderbookEventArgs> OrderbookEventHandler;
        public event EventHandler<TradeEventArgs> TradeEventHandler;
        public event EventHandler<UnsolicitedEventArgs> UnsolicitedEventHandler;     

        public BybitTradeSocket(LogFactory logFactory, string apiKey = null, string apiSecret = null, bool liveEnvironment = true) : base(apiKey, apiSecret, liveEnvironment)
        {
            _logger = logFactory.GetCurrentClassLogger();
            _tradeReceiverSemaphore = new SemaphoreSlim(1, 1);
            _orderbookUpdateSemaphore = new SemaphoreSlim(1, 1);
            _orderbookSnapshotSemaphore = new SemaphoreSlim(1, 1);

            _tradeReceiverBuffer = new Dictionary<string, BybitDerivativesTradeUpdate>();
            _orderbooks = new Dictionary<string, BybitDerivativesOrderBookEntry>();
        }

        public async void SubscribeToTradesAsync(IEnumerable<string> symbols)
        {
            _logger.Info("Subscribing to receive trades.");

            CallResult<UpdateSubscription> response = await _socket.DerivativesApi.SubscribeToTradesUpdatesAsync(StreamDerivativesCategory.USDTPerp, symbols, CallbackTradesReceive);

            if (!response.GetResultOrError(out UpdateSubscription updateSubscription, out Error error))
            {
                throw new Exception($"Failed to subscribe to receive trades. Error: ({error?.Code}) {error?.Message}.");
            }

            updateSubscription.ConnectionRestored += TradeReceiverConnectionRestored;
            updateSubscription.ConnectionLost += TradeReceiverConnectionLost;
            updateSubscription.ConnectionClosed += TradeReceiverConnectionClosed;
        }

        public async void SubscribeToOrderbookAsync(IEnumerable<string> symbols, int depth)
        {
            _logger.Info("Subscribing to receive orderbook.");

            CallResult<UpdateSubscription> response = await _socket.DerivativesApi.SubscribeToOrderBooksUpdatesAsync(StreamDerivativesCategory.USDTPerp, symbols, depth, CallbackOrderbookSnapshot, CallbackOrderbookUpdate);

            if (!response.GetResultOrError(out UpdateSubscription updateSubscription, out Error error))
            {
                throw new Exception($"Failed to subscribe to receive trades. Error: ({error?.Code}) {error?.Message}.");
            }
        }

        private void TradeReceiverConnectionRestored(TimeSpan obj)
        {
            InvokeUnsolicitedEvent(MessageType.Info, "Trade receiver connection restored.");
        }

        private void TradeReceiverConnectionLost()
        {
            InvokeUnsolicitedEvent(MessageType.Error, "Trade receiver connection lost.");
        }

        private void TradeReceiverConnectionClosed()
        {
            InvokeUnsolicitedEvent(MessageType.Warning, "Trade receiver connection closed.");
        }

        private void CallbackTradesReceive(DataEvent<IEnumerable<BybitDerivativesTradeUpdate>> tradesReceived)
        {
            try
            {
                _tradeReceiverSemaphore.WaitAsync();

                if (!ExtractTradesReceivedData(tradesReceived.Topic, tradesReceived.Data, out List<Trade> trades))
                    return;

                InvokeTradeReceiverEvent(tradesReceived.Topic, trades);
            }
            catch (Exception e)
            {
                _logger.Error($"Failed CallbackTradesReceived. {e}");
            }
            finally
            {
                _tradeReceiverSemaphore.Release();
            }
        }

        private void CallbackOrderbookSnapshot(DataEvent<BybitDerivativesOrderBookEntry> orderbookEntry)
        {
            try
            {
                _orderbookSnapshotSemaphore.WaitAsync();

                if (!ExtractOrderbookReceivedData(orderbookEntry.Topic, orderbookEntry.Data, out Orderbook orderbook))
                    return;

                InvokeOrderbookEvent(orderbook.Symbol, orderbook);
            }
            catch (Exception e)
            {
                _logger.Error($"Failed CallbackOrderbookSnapshot. {e}");
            }
            finally
            {
                _orderbookSnapshotSemaphore.Release();
            }
        }

        private void CallbackOrderbookUpdate(DataEvent<BybitDerivativesOrderBookEntry> orderbookEntry)
        {
            try
            {
                _orderbookUpdateSemaphore.WaitAsync();

                if (!ExtractOrderbookReceivedData(orderbookEntry.Topic, orderbookEntry.Data, out Orderbook orderbook))
                    return;

                InvokeOrderbookEvent(orderbook.Symbol, orderbook);
            }
            catch (Exception e)
            {
                _logger.Error($"Failed CallbackOrderbookUpdate. {e}");
            }
            finally
            {
                _orderbookUpdateSemaphore.Release();
            }
        }

        private bool ExtractOrderbookReceivedData(string symbol, BybitDerivativesOrderBookEntry orderbookEntry, out Orderbook orderbook)
        {
            orderbook = null;

            if (orderbookEntry == null) return false;

            if (!_orderbooks.TryGetValue(symbol, out BybitDerivativesOrderBookEntry o))
            {
                _orderbooks.Add(symbol, orderbookEntry);
                return false;
            }

            if (!orderbookEntry.Bids.IsNullOrEmpty())
            {
                List<BybitUnifiedMarginOrderBookItem> bids = o.Bids.ToList();

                for (int i = 0; i < orderbookEntry.Bids.Count(); i++)
                {
                    if (i >= bids.Count())
                    {
                        // overhead detected
                        break;
                    }

                    bids[i] = orderbookEntry.Bids.ElementAt(i);
                }

                o.Bids = bids;
            }

            if (!orderbookEntry.Asks.IsNullOrEmpty())
            {
                List<BybitUnifiedMarginOrderBookItem> asks = o.Asks.ToList();

                for (int i = 0; i < orderbookEntry.Asks.Count(); i++)
                {
                    if (i >= asks.Count())
                    {
                        // overhead detected
                        break;
                    }

                    asks[i] = orderbookEntry.Asks.ElementAt(i);
                }

                o.Asks = asks;
            }

            orderbook = new Orderbook();
            orderbook.Symbol = symbol;
            orderbook.Bids = new List<Bid>();

            foreach (var b in o.Bids)
            {
                orderbook.Bids.Add(new Bid(b.Price, b.Quantity));
            }

            orderbook.Asks = new List<Ask>();

            foreach (var a in o.Asks)
            {
                orderbook.Asks.Add(new Ask(a.Price, a.Quantity));
            }

            return true;
        }

        private bool ExtractTradesReceivedData(string symbol, IEnumerable<BybitDerivativesTradeUpdate> tradesReceived, out List<Trade> trades)
        {
            trades = null;

            try
            {
                if (tradesReceived.IsNullOrEmpty())
                {
                    _logger.Warn("Failed to extract trades received data. No trades received.");
                    return false;
                }

                trades = new List<Trade>();

                foreach (BybitDerivativesTradeUpdate tr in tradesReceived)
                {
                    Trade trade = new Trade()
                    {
                        Id = tr.Id,
                        Symbol = tr.Symbol,
                        TradeDirection = (TradeDirection)(int)tr.Side,
                        Time = tr.TradeTime,
                        Price = tr.Price,
                        DeltaPrice = ProvideTradeDeltaPrice(symbol, tr),
                        Volume = tr.Quantity
                    };

                    trades.Add(trade);

                    SetLatestTradeReceived(symbol, tr);
                }

                return true;
            }
            catch(Exception e)
            {
                _logger.Error($"Failed ExtractTradeUpdateData. {e}");
                return false;
            }
        }

        private void InvokeTradeReceiverEvent(string symbol, List<Trade> trades)
        {
            if (TradeEventHandler != null)
            {
                foreach (var attached in TradeEventHandler.GetInvocationList())
                {
                    Task.Run(() =>
                    {
                        attached.DynamicInvoke(this, new TradeEventArgs(symbol, trades));
                    })
                    .ContinueWith(t =>
                    {
                        _logger.Error(t.Exception, "Failed in InvokeTradeReceiverEvent.");
                    }, TaskContinuationOptions.OnlyOnFaulted);
                }
            }
        }

        private void InvokeOrderbookEvent(string symbol, Orderbook orderbook)
        {
            if (OrderbookEventHandler != null)
            {
                foreach (var attached in OrderbookEventHandler.GetInvocationList())
                {
                    Task.Run(() =>
                    {
                        attached.DynamicInvoke(this, new OrderbookEventArgs(symbol, orderbook));
                    })
                    .ContinueWith(t =>
                    {
                        _logger.Error(t.Exception, "Failed in InvokeOrderbookEvent.");
                    }, TaskContinuationOptions.OnlyOnFaulted);
                }
            }
        }

        private void InvokeUnsolicitedEvent(MessageType messageType, string message)
        {
            if (UnsolicitedEventHandler != null)
            {
                foreach (var attached in UnsolicitedEventHandler.GetInvocationList())
                {
                    Task.Run(() =>
                    {
                        attached.DynamicInvoke(this, new UnsolicitedEventArgs(messageType, message));
                    })
                    .ContinueWith(t =>
                    {
                        _logger.Error(t.Exception, "Failed in InvokeUnsolicitedEvent.");
                    }, TaskContinuationOptions.OnlyOnFaulted);
                }
            }
        }

        private bool GetLatestTradeReceived(string symbol, out BybitDerivativesTradeUpdate trade)
        {
            return _tradeReceiverBuffer.TryGetValue(symbol, out trade);        
        }

        private void SetLatestTradeReceived(string symbol, BybitDerivativesTradeUpdate trade)
        {
            if (!_tradeReceiverBuffer.TryGetValue(symbol, out _))
            {
                _tradeReceiverBuffer.Add(symbol, trade);
            }
            else
            {
                _tradeReceiverBuffer[symbol] = trade;
            }
        }

        private decimal ProvideTradeDeltaPrice(string symbol, BybitDerivativesTradeUpdate trade)
        {
            if (trade == null)
            {
                _logger.Warn($"Failed to provide trade delta price. No {symbol} trade.");
                return -1;
            }

            if (!GetLatestTradeReceived(symbol, out BybitDerivativesTradeUpdate latestTrade))
            {
                _logger.Info($"No {symbol} latest trade received. Initial trade delta price is 0.");
                return 0;
            }

            if (latestTrade.Side != trade.Side)
            {
                return 0;
            }

            return trade.Price - latestTrade.Price;
        }
    }
}
