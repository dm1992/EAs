using Bybit.Net.Enums;
using Bybit.Net.Objects.Models.Derivatives;
using Bybit.Net.Objects.Models.Socket.Derivatives;
using Common;
using CryptoExchange.Net.Objects;
using CryptoExchange.Net.Sockets;
using MarketClient.Interfaces.Socket;
using MarketProxy.Events;
using NLog;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace MarketProxy.Socket.BybitSocket
{
    public class BybitPriceSocket : BybitBaseSocket, IPriceSocket
    {
        private readonly ILogger _logger;
        private readonly SemaphoreSlim _priceReceiverSnapshotSemaphore;
        private readonly SemaphoreSlim _priceReceiverUpdateSemaphore;

        public event EventHandler<PriceEventArgs> PriceEventHandler;
        public event EventHandler<UnsolicitedEventArgs> UnsolicitedEventHandler;

        public BybitPriceSocket(LogFactory logFactory, string apiKey = null, string apiSecret = null, bool liveEnvironment = true) : base(apiKey, apiSecret, liveEnvironment)
        {
            _logger = logFactory.GetCurrentClassLogger();
            _priceReceiverSnapshotSemaphore = new SemaphoreSlim(1, 1);
            _priceReceiverUpdateSemaphore = new SemaphoreSlim(1, 1);
        }

        public async void SubscribeToPricesAsync(IEnumerable<string> symbols)
        {
            _logger.Info("Subscribing to receive prices.");

            CallResult<UpdateSubscription> response = await _socket.DerivativesApi.SubscribeToTickersUpdatesAsync(StreamDerivativesCategory.USDTPerp, symbols, CallbackPriceReceiveSnapshot, CallbackPriceReceiveUpdate);

            if (!response.GetResultOrError(out UpdateSubscription updateSubscription, out Error error))
            {
                throw new Exception($"Failed to subscribe to receive prices. Error: ({error?.Code}) {error?.Message}.");
            }

            updateSubscription.ConnectionRestored += PriceReceiverConnectionRestored;
            updateSubscription.ConnectionLost += PriceReceiverConnectionLost;
            updateSubscription.ConnectionClosed += PriceReceiverConnectionClosed;
        }

        private void CallbackPriceReceiveSnapshot(DataEvent<BybitDerivativesTicker> ticker)
        {
            try
            {
                _priceReceiverSnapshotSemaphore.WaitAsync();

                if (ticker.Data.LastPrice != 0)
                {
                    _logger.Info($"{ticker.Data.Symbol} price: {ticker.Data.LastPrice}$.");

                    InvokePriceReceiverEvent(ticker.Data.Symbol, ticker.Data.LastPrice);
                }

            }
            catch (Exception e)
            {
                _logger.Error($"Failed CallbackPriceReceiveSnapshot. {e}");
            }
            finally
            {
                _priceReceiverSnapshotSemaphore.Release();
            }
        }

        private void CallbackPriceReceiveUpdate(DataEvent<BybitDerivativesTickerUpdate> tickerUpdate)
        {
            try
            {
                _priceReceiverUpdateSemaphore.WaitAsync();

                if (tickerUpdate.Data.LastPrice != 0)
                {
                    _logger.Info($"{tickerUpdate.Data.Symbol} price: {tickerUpdate.Data.LastPrice}$.");

                    InvokePriceReceiverEvent(tickerUpdate.Data.Symbol, tickerUpdate.Data.LastPrice);
                }

            }
            catch (Exception e)
            {
                _logger.Error($"Failed CallbackPriceReceiveSnapshot. {e}");
            }
            finally
            {
                _priceReceiverUpdateSemaphore.Release();
            }
        }

        private void PriceReceiverConnectionRestored(TimeSpan obj)
        {
            InvokeUnsolicitedEvent(MessageType.Info, "Price receiver connection restored.");
        }

        private void PriceReceiverConnectionLost()
        {
            InvokeUnsolicitedEvent(MessageType.Error, "Price receiver connection lost.");
        }

        private void PriceReceiverConnectionClosed()
        {
            InvokeUnsolicitedEvent(MessageType.Warning, "Price receiver connection closed.");
        }

        private void InvokePriceReceiverEvent(string symbol, decimal price)
        {
            if (PriceEventHandler != null)
            {
                foreach (var attached in PriceEventHandler.GetInvocationList())
                {
                    Task.Run(() =>
                    {
                        attached.DynamicInvoke(this, new PriceEventArgs(symbol, price));
                    })
                    .ContinueWith(t =>
                    {
                        _logger.Error(t.Exception, "Failed in InvokePriceReceiverEvent.");
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
    }
}
