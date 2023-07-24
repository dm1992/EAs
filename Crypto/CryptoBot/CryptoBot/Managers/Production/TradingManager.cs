using Bybit.Net;
using Bybit.Net.Clients;
using Bybit.Net.Clients.V5;
using Bybit.Net.Enums;
using Bybit.Net.Objects.Models.Spot;
using Bybit.Net.Objects.Models.Spot.v3;
using Bybit.Net.Objects.Models.V5;
using CryptoBot.Interfaces.Managers;
using CryptoBot.Models;
using CryptoExchange.Net.Authentication;
using CryptoExchange.Net.Objects;
using CryptoExchange.Net.Sockets;
using Microsoft.Extensions.Logging;
using NLog;
using NLog.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace CryptoBot.Managers.Production
{

    public class TradingManager : ITradingManager
    {
        private const int TRADING_SERVER_PING_DELAY = 5000;
        private const int BALANCE_MONITOR_DELAY = 60000;

        private readonly BybitRestClient _client;
        private readonly Config _config;
        private readonly SemaphoreSlim _tradingServerSemaphore;
        private readonly SemaphoreSlim _balanceSemaphore;

        private NLog.ILogger _logger;
        private bool _isInitialized;

        public TradingManager(LogFactory logFactory, Config config)
        {
            _config = config;
            _logger = logFactory.GetCurrentClassLogger();
            _tradingServerSemaphore = new SemaphoreSlim(1, 1);
            _balanceSemaphore = new SemaphoreSlim(1, 1);

            _client = new BybitRestClient(optionsDelegate => 
                                          {
                                              optionsDelegate.Environment = BybitEnvironment.Live; 
                                              optionsDelegate.AutoTimestamp = true; 
                                          });

            _client.SetApiCredentials(new ApiCredentials(_config.ApiKey, _config.ApiSecret));

            _isInitialized = false;
        }

        public bool Initialize()
        {
            try
            {
                if (_isInitialized) return true;

                Task.Run(() => { PingTradingServer(); });
                Task.Run(() => { MonitorBalances(); });

                _logger.Info("Initialized.");

                _isInitialized = true;
                return true;
            }
            catch (Exception e)
            {
                _logger.Error($"Initialization failed. {e}");
                return false;
            }
        }

        public async Task<bool> TradingServerAvailable()
        {
            try
            {
                await _tradingServerSemaphore.WaitAsync();

                var response = await _client.V5Api.ExchangeData.GetServerTimeAsync();

                if (!response.Success)
                {
                    _logger.Error($"Trading server unavailable. Error code: {response.Error.Code}. Error message: {response.Error.Message}.");
                    return false;
                }

                return true;
            }
            finally
            {
                _tradingServerSemaphore.Release();
            }
        }

        public async Task<IEnumerable<BybitSpotBalance>> GetBalances()
        {
            try
            {
                await _balanceSemaphore.WaitAsync();

                var response = await _client.SpotApiV3.Account.GetBalancesAsync();

                if (!response.Success)
                {
                    _logger.Error($"Failed to get balances. Error code: {response.Error.Code}. Error message: {response.Error.Message}.");
                    return null;
                }

                return response.Data;
            }
            finally
            {
                _balanceSemaphore.Release();
            }
        }

        public async Task<bool> PlaceOrder(BybitSpotOrderV3 order)
        {
            if (order == null)
                return false;

            var response = await _client.SpotApiV3.Trading.PlaceOrderAsync(order.Symbol, order.Side, OrderType.Market, Math.Round(order.Quantity, 8), null, TimeInForce.GoodTillCanceled);

            if (!response.Success)
            {
                _logger.Error($"Failed to place order for symbol {order.Symbol}. Error code: {response.Error.Code}. Error message: {response.Error.Message}.");
                return false;
            }

            order.ClientOrderId = response.Data.ClientOrderId;
            order.Id = response.Data.Id;

            return true;
        }

        public async Task<BybitSpotOrderV3> GetOrder(string orderId)
        {
            if (String.IsNullOrEmpty(orderId))
                return null;

            var response = await _client.SpotApiV3.Trading.GetOrderAsync(Convert.ToInt64(orderId));

            if (!response.Success)
            {
                _logger.Error($"Failed to get order for order id {orderId}. Error code: {response.Error.Code}. Error message: {response.Error.Message}.");
                return null;
            }

            return response.Data;
        }


        #region Workers

        private async void PingTradingServer()
        {
            _logger.Debug("PingTradingServer started.");

            try
            {
                int ping = 0;

                while (true)
                {

                    if (!await TradingServerAvailable())
                    {
                        _logger.Warn("Failed to ping trading server.");
                    }

                    if(++ping % 10 == 0)
                    {
                        _logger.Info("Trading server is alive.");
                    }
                   
                    Task.Delay(TRADING_SERVER_PING_DELAY).Wait();
                }
            }
            catch (Exception e)
            {
                _logger.Error($"Failed PingTradingServer. {e}");
            }
        }

        private async void MonitorBalances()
        {
            _logger.Debug("MonitorBalances started.");

            try
            {
                while (true)
                {
                    var balances = await GetBalances();

                    if (!balances.IsNullOrEmpty())
                    {
                        foreach (var balance in balances)
                        {
                            _logger.Info($"{balance.Asset} balance. Total: {balance.Total}$, Locked: {balance.Locked}$, Available: {balance.Available}$.");
                        }
                    }
                  
                    Task.Delay(BALANCE_MONITOR_DELAY).Wait();
                }
            }
            catch (Exception e)
            {
                _logger.Error($"Failed MonitorBalances. {e}");
            }
        }

        #endregion

    }
}
