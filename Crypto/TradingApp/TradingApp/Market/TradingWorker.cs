using Bybit.Net.Clients;
using Bybit.Net.Enums;
using Bybit.Net.Objects;
using Bybit.Net.Objects.Models.Spot;
using CryptoExchange.Net.Objects;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TradingApp.Data;
using TradingApp.EventArgs;
using TradingApp.Interfaces;

namespace TradingApp.Market
{
    public class TradingWorker : IApplicationEvent
    {
        private readonly BybitClient _bybitClient;
        private readonly Config _config;

        private CandleAnalyzer _candleAnalyzer;
        private ManualResetEvent _tradingServerAvailable;
        private List<TradingSignal> _tradingSignals;
        private IEnumerable<BybitSpotOrder> _openOrders;
        private IEnumerable<BybitSpotBalance> _initialBalances;

        public event EventHandler<ApplicationEventArgs> ApplicationEvent;

        public TradingWorker(CandleAnalyzer tradingSignalProvider, Config config)
        {
            _candleAnalyzer = tradingSignalProvider;
            _tradingServerAvailable = new ManualResetEvent(initialState: true);
            _tradingSignals = new List<TradingSignal>();
            _config = config;

            BybitClientOptions options = BybitClientOptions.Default;
            options.ApiCredentials = new CryptoExchange.Net.Authentication.ApiCredentials(_config.ApiKey, _config.ApiSecret);
            _bybitClient = new BybitClient(options);

            _candleAnalyzer.TradingSignalEvent += TradingSignalEventHandler;

            Task.Run(() => CheckTradingServerConnectionThread());

            Task.Run(() => TradingWorkerThread());
        }

        private async Task CheckTradingServerConnectionThread()
        {
            try
            {
                while (true)
                {
                    var serverTime = await GetServerTimeAsync();
                    if (serverTime.Success)
                    {
                        // just heartbeat message
                        SendNotification(EventType.INFORMATION, $"Connected to trading server [{serverTime.Data}].", mailNotification: false);
                        _tradingServerAvailable.Set();
                    }
                    else
                    {
                        SendNotification(EventType.ERROR, "Failed to connect to trading server. Trading worker has stopped.");
                        _tradingServerAvailable.Reset();
                    }

                    await Task.Delay(60000);
                }
            }
            catch (Exception e)
            {
                SendNotification(EventType.ERROR, e.Message);
            }
        }

        private async Task TradingWorkerThread()
        {
            try
            {
                _initialBalances = await GetBalancesAsync();
                if (_initialBalances == null)
                {
                    throw new Exception("Initial wallet balances unknown. Can't start trading.");
                }

                while (true)
                {
                    if (!_tradingServerAvailable.WaitOne(0) || !_tradingSignals.Where(x => !x.MarkAsForgotten).Any())
                    {
                        // temporary skip trading, tech issue or without new signals
                        continue;
                    }

                    await GetOpenOrdersAsync();

                    if (await StopTradingAsync()) break;

                    await PlaceOrdersAsync();
                }
            }
            catch (Exception e)
            {
                SendNotification(EventType.ERROR, e.Message);
            }
        }


        #region Trading API wrapper 

        private async Task<WebCallResult<DateTime>> GetServerTimeAsync()
        {
            return await _bybitClient.InverseFuturesApi.ExchangeData.GetServerTimeAsync();
        }

        private async Task<IEnumerable<BybitSpotBalance>> GetBalancesAsync()
        {
            var balances = await _bybitClient.SpotApi.Account.GetBalancesAsync();
            if (!balances.Success)
            {
                SendNotification(EventType.WARNING, $"Failed to get wallet balances!!!");
                return null;
            }

            return balances.Data;
        }

        private async Task GetOpenOrdersAsync()
        {
            var openOrders = await _bybitClient.SpotApi.Trading.GetOpenOrdersAsync();
            if (!openOrders.Success)
            {
                SendNotification(EventType.WARNING, $"Failed to get open orders!!!");
                return;
            }

            _openOrders = openOrders.Data;
        }

        private async Task PlaceOrdersAsync()
        {
            foreach (var tsGroup in _tradingSignals.Where(x => !x.MarkAsForgotten).GroupBy(x => x.Symbol))
            {
                if (_openOrders.Where(x => x.Symbol == tsGroup.Key).Count() >= _config.OpenOrdersPerSymbol)
                {
                    // skip symbol
                    continue;
                }

                foreach (var ts in tsGroup)
                {
                    var order = await _bybitClient.SpotApi.Trading.PlaceOrderAsync(ts.Symbol, ts.Direction == TradingDirection.BUY ? OrderSide.Buy : OrderSide.Sell, OrderType.Market, 50, null, null, ts.InternalId.ToString());
                    if (!order.Success)
                    {
                        ts.RealizationAttempts++;

                        SendNotification(EventType.WARNING, $"Failed to place order for signal id {ts.InternalId} ({ts.RealizationAttempts}. attempt). " +
                                                            $"Error code: {order.Error.Code}. Error message: {order.Error.Message}!!!");
                        if (ts.RealizationAttempts >= 10)
                        {
                            // check api for errors, raise error event?
                            ts.MarkAsForgotten = true;
                        }

                        continue;
                    }

                    SendNotification(EventType.INFORMATION, $"Placed order id {order.Data.Id} for signal id {ts.InternalId}.");
                    ts.MarkAsForgotten = true;
                }
            }
        }

        private async Task<bool> StopTradingAsync()
        {
            //xxx based on profit, time, etc trigger stop trading
            var balances = await GetBalancesAsync();
            if (balances == null) return false;

            bool assetBalancesGrown = false;
            foreach (var b in balances)
            {
                var initialAssetBalance = _initialBalances.FirstOrDefault(x => x.Asset == b.Asset);
                assetBalancesGrown = initialAssetBalance != null && b.Available >= initialAssetBalance.Available * (decimal)1.05; //xxx total wallet growth reached 5%
            }

            if (assetBalancesGrown)
            {
                SendNotification(EventType.STOP_TRADING, "Requested stop trading.");
                return true;
            }

            return false;
        }

        #endregion


        private void TradingSignalEventHandler(object sender, TradingSignalEventArgs args)
        {
            SendNotification(EventType.INFORMATION, args.TradingSignal.ToString());

            _tradingSignals.Add(args.TradingSignal);
        }

        private void SendNotification(EventType type, string message, bool mailNotification = true)
        {
            ApplicationEvent?.Invoke(this, new ApplicationEventArgs(type, message));

            if (!mailNotification) return;

            //add nuget notification manager for email alerting, info...
        }
    }
}
