using Bybit.Net.Clients;
using Bybit.Net.Objects;
using Bybit.Net.Objects.Models;
using Bybit.Net.Objects.Models.Socket.Spot;
using CryptoBot.Data;
using CryptoBot.Data.Davor;
using CryptoBot.EventArgs;
using CryptoBot.Interfaces;
using CryptoExchange.Net.Sockets;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CryptoBot.Managers.Davor_old
{
    public class MarketManager : IMarketManager
    {
        private readonly ITradingAPIManager _tradingAPIManager;
        private readonly Config _config;
        private readonly SemaphoreSlim _tradeSemaphore;
        private readonly SemaphoreSlim _orderBookSemaphore;

        private List<UpdateSubscription> _subscriptions;
        private List<DataEvent<BybitSpotTradeUpdate>> _trades;
        private List<PassiveMarket> _passiveMarkets;

        public event EventHandler<ApplicationEventArgs> ApplicationEvent;

        public MarketManager(ITradingAPIManager tradingAPIManager, Config config)
        {
            _tradingAPIManager = tradingAPIManager;
            _config = config;
            _tradeSemaphore = new SemaphoreSlim(1, 1);
            _orderBookSemaphore = new SemaphoreSlim(1, 1);

            _subscriptions = new List<UpdateSubscription>();
            _trades = new List<DataEvent<BybitSpotTradeUpdate>>();
            _passiveMarkets = new List<PassiveMarket>();
        }

        public void InvokeAPISubscription()
        {
            ApplicationEvent?.Invoke(this, new ApplicationEventArgs(EventType.Information,
            $"Invoked API subscription in market manager."));

            BybitSocketClientOptions socketClientOptions = BybitSocketClientOptions.Default;
            socketClientOptions.SpotStreamsV2Options.BaseAddress = _config.SpotStreamEndpoint;

            BybitSocketClient socketClient = new BybitSocketClient(socketClientOptions);

            foreach (var symbol in _config.Symbols)
            {
                _subscriptions.Add(socketClient.SpotStreamsV2.SubscribeToTradeUpdatesAsync(symbol, HandleTrade).GetAwaiter().GetResult().Data); // deadlock issue, async method in sync manner
                _subscriptions.Add(socketClient.SpotStreamsV2.SubscribeToOrderBookUpdatesAsync(symbol, HandleOrderBook).GetAwaiter().GetResult().Data); //xxx change level
            }

            foreach (var subscription in _subscriptions)
            {
                subscription.ConnectionRestored += API_Subscription_ConnectionRestored;
                subscription.ConnectionLost += API_Subscription_ConnectionLost;
                subscription.ConnectionClosed += API_Subscription_ConnectionClosed;
            }
        }

        public async void CloseAPISubscription()
        {
            foreach (var subscription in _subscriptions)
            {
                await subscription.CloseAsync();
            }
        }

        public async Task<IMarket> GetCurrentMarket(string symbol)
        {
            decimal? symbolLatestPrice = await _tradingAPIManager.GetPriceAsync(symbol);
            if (!symbolLatestPrice.HasValue)
                return null;

            lock (_trades)
            {
                var symbolTrades = _trades.Where(x => x.Topic == symbol);
                if (symbolTrades.Count() < _config.TradeLimit)
                    return null;

                lock (_passiveMarkets)
                {
                    AggressiveMarket aggressiveMarket = new AggressiveMarket(symbol, symbolTrades.OrderByDescending(x => x.Data.Timestamp).Take(_config.TradeLimit).ToList(), _config.AggressiveVolumePercentage);
                    PassiveMarket passiveMarket = _passiveMarkets.Where(x => x.Symbol == symbol.ToLower()).OrderByDescending(x => x.CreatedAt).FirstOrDefault();

                    return new Market(symbol, aggressiveMarket, passiveMarket, symbolLatestPrice.Value, _config.TotalVolumePercentage);
                }
            }
        }


        #region Event handlers

        private void API_Subscription_ConnectionRestored(TimeSpan obj)
        {
            ApplicationEvent?.Invoke(this, new ApplicationEventArgs(EventType.Information,
            $"API subscription connection restored in market manager."));
        }

        private void API_Subscription_ConnectionLost()
        {
            ApplicationEvent?.Invoke(this, new ApplicationEventArgs(EventType.Information,
            $"API subscription connection lost in market manager."));
        }

        private void API_Subscription_ConnectionClosed()
        {
            ApplicationEvent?.Invoke(this, new ApplicationEventArgs(EventType.Information,
            $"API subscription connection closed in market manager."));
        }

        private void HandleTrade(DataEvent<BybitSpotTradeUpdate> trade)
        {
            try
            {
                _tradeSemaphore.WaitAsync();

                lock (_trades)
                {
                    _trades.Add(trade);
                }
            }
            catch (Exception e)
            {
                ApplicationEvent?.Invoke(this, new ApplicationEventArgs(EventType.Error,
                $"!!!HandleTrade failed!!! {e}"));
            }
            finally
            {
                _tradeSemaphore.Release();
            }
        }

        private void HandleOrderBook(DataEvent<BybitSpotOrderBookUpdate> orderBook)
        {
            try
            {
                _orderBookSemaphore.WaitAsync();

                lock (_passiveMarkets)
                {
                    _passiveMarkets.Add(new PassiveMarket(orderBook.Topic, orderBook.Data.Bids, orderBook.Data.Asks, _config.PassiveVolumePercentage));
                }
            }
            catch (Exception e)
            {
                ApplicationEvent?.Invoke(this, new ApplicationEventArgs(EventType.Error,
                $"!!!HandleOrderBook failed!!! {e}"));
            }
            finally
            {
                _orderBookSemaphore.Release();
            }
        }

        public void StartCandleBatchMonitor()
        {
            throw new NotImplementedException();
        }

        public void StopCandleBatchMonitor()
        {
            throw new NotImplementedException();
        }

        public bool Initialize()
        {
            throw new NotImplementedException();
        }

        #endregion
    }
}
