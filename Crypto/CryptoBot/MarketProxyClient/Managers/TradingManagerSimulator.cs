using Common;
using MarketClient.Interfaces.Api;
using MarketClient.Interfaces.Socket;
using MarketProxyClient.Interfaces;
using MarketProxyClient.Models;
using NLog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace MarketProxyClient.Managers
{
    public class TradingManagerSimulator : ITradingManager
    {
        private const int MONITOR_WALLET_BALANCE_DELAY = 60000;

        private readonly IExchangeApi _exchangeApi;
        private readonly IPriceSocket _priceSocket;
        private readonly IMarketSignalProvider _marketSignalProvider;
        private readonly Config _config;
        private readonly ILogger _logger;
        private readonly SemaphoreSlim _priceSemaphore;
        private readonly SemaphoreSlim _marketSignalSemaphore;
        private readonly SemaphoreSlim _walletBalanceSemaphore;

        private Dictionary<string, List<ITrade>> _tradesBuffer;

        private bool _isInitialized;

        public TradingManagerSimulator(
            IExchangeApi exchangeApi,
            IPriceSocket priceSocket,
            IMarketSignalProvider marketSignalProvider, 
            Config config, 
            LogFactory logFactory)
        {
            _exchangeApi = exchangeApi;
            _priceSocket = priceSocket;
            _marketSignalProvider = marketSignalProvider;
            _config = config;
            _logger = logFactory.GetCurrentClassLogger();
            _priceSemaphore = new SemaphoreSlim(1, 1);
            _marketSignalSemaphore = new SemaphoreSlim(1, 1);
            _walletBalanceSemaphore = new SemaphoreSlim(1, 1);

            _isInitialized = false;
            _tradesBuffer = new Dictionary<string, List<ITrade>>();
        }

        public bool Initialize()
        {
            if(_isInitialized)
                return true;

            _logger.Info("Initializing.");

            Task.Run(() => MonitorWalletBalanceInThread())
            .ContinueWith(t =>
            {
                _logger.Error(t.Exception, "Failed in MonitorWalletBalanceInThread.");
            }, TaskContinuationOptions.OnlyOnFaulted);

            SubscribeToMarketSignal();

            SubscribeToPrices();

            _logger.Info("Initialized.");
            return _isInitialized = true;
        }

        private void SubscribeToMarketSignal()
        {
            if (_marketSignalProvider != null)
            {
                _marketSignalProvider.MarketSignalEventHandler += MarketSignalEventHandler;
            }
        }

        private void SubscribeToPrices()
        {
            if (_priceSocket != null)
            {

                _priceSocket.PriceEventHandler += PriceReceiverEventHandler;
                _priceSocket.UnsolicitedEventHandler += PriceReceiverUnsolicitedEventHandler;

                _priceSocket.SubscribeToPricesAsync(_config.Symbols);
            }
        }

        private void PriceReceiverEventHandler(object sender, MarketProxy.Events.PriceEventArgs e)
        {
            try
            {
                _priceSemaphore.Wait();

                HandlePriceEvent(e);
            }
            finally
            {
                _priceSemaphore.Release();
            }
        }

        private void PriceReceiverUnsolicitedEventHandler(object sender, MarketProxy.Events.UnsolicitedEventArgs e)
        {
            //xxx log for now
            switch (e.MessageType)
            {
                case MessageType.Info:
                    _logger.Info($"'{e}'");
                    break;

                case MessageType.Warning:
                    _logger.Warn($"'{e}'");
                    break;

                case MessageType.Error:
                    _logger.Error($"'{e}'");
                    break;

                default:
                    _logger.Debug($"'{e}'");
                    break;
            }
        }

        public bool CreateTrade(ITradeArgs tradeArgs, out ITrade trade)
        {
            trade = null;

            if (tradeArgs == null)
            {
                _logger.Error($"Failed to create trade. Missing trade args.");
                return false;
            }

            trade = new Trade();
            trade.Symbol = tradeArgs.Symbol;
            trade.MarketDirection = tradeArgs.MarketDirection;
            trade.MarketEvaluation = tradeArgs.MarketEvaluation;
            trade.OpenPrice = tradeArgs.Price;
            trade.TakeProfit = tradeArgs.TakeProfitPrice;
            trade.StopLoss = tradeArgs.StopLossPrice;

            _logger.Info($"Created {trade.Symbol} {trade.MarketDirection} trade ({trade.Id}) @ open price: {trade.OpenPrice}$ (take profit: {trade.TakeProfit}, stop loss: {trade.StopLoss}).");
            return true;
        }

        public bool CloseTrade(ITradeArgs tradeArgs, out ITrade trade)
        {
            trade = null;

            if (tradeArgs == null)
            {
                _logger.Error($"Failed to close trade. Missing trade args.");
                return false;
            }

            if (!GetTradeFromBuffer(tradeArgs.Symbol, tradeArgs.Id, out trade))
            {
                _logger.Warn($"Failed to get {tradeArgs.Symbol} ({tradeArgs.Id}) trade from buffer.");
                return false;
            }

            trade.ClosePrice = tradeArgs.Price;
            trade.ClosedAt = DateTime.Now;

            _logger.Info($"Closed {trade.Symbol} {trade.MarketDirection} trade ({trade.Id}) @ close price: {trade.ClosePrice}$ " +
                         $"(open price: {trade.OpenPrice}$, balance: {trade.Balance}$, duration: {trade.Duration}).");
            return true;
        }    

        public decimal GetWalletBalance()
        {
            try
            {
                _walletBalanceSemaphore.Wait();

                decimal balance = 0;
                foreach (string symbol in _config.Symbols)
                {
                    if (GetTradesFromBuffer(symbol, out List<ITrade> trades))
                    {
                        balance += trades.Where(x => !x.IsActive).Sum(x => x.Balance);          
                    }
                }

                return balance;
            }
            finally
            {
                _walletBalanceSemaphore.Release();
            }
        }

        private void MarketSignalEventHandler(object sender, Events.MarketSignalEventArgs e)
        {
            try
            {
                _marketSignalSemaphore.Wait();

                HandleMarketSignalEvent(e);
            }
            finally
            {
                _marketSignalSemaphore.Release();
            }
        }

        private void HandlePriceEvent(MarketProxy.Events.PriceEventArgs e)
        {
            if (e == null) return;

            if (GetTradesFromBuffer(e.Symbol, out List<ITrade> trades))
            {
                TradeConfig symbolTradeConfig = _config.TradeConfigs.First(x => x.Symbol == e.Symbol);

                foreach (var activeTrade in trades.Where(x => x.IsActive))
                {
                    bool closeTrade = false;

                    if (activeTrade.MarketDirection == MarketDirection.Buy)
                    {
                        if (e.Price >= activeTrade.TakeProfit || e.Price <= activeTrade.StopLoss)
                        {
                            closeTrade = true;
                        }
                    }
                    else if (activeTrade.MarketDirection == MarketDirection.Sell)
                    {
                        if (e.Price <= activeTrade.TakeProfit || e.Price >= activeTrade.StopLoss)
                        {
                            closeTrade = true;
                        }
                    }

                    if (closeTrade)
                    {
                        TradeArgs tradeArgs = new TradeArgs(activeTrade.Id, activeTrade.Symbol);
                        tradeArgs.Price = e.Price;

                        if (CloseTrade(tradeArgs, out ITrade trade))
                        {
                            SaveTradeToFile(tradeArgs.Symbol, trade);
                        }
                    }
                }
            }
        }

        private void HandleMarketSignalEvent(Events.MarketSignalEventArgs e)
        {
            if (e == null) return;

            decimal? price = _exchangeApi.GetPrice(e.Symbol).Result;
            if (!price.HasValue)
            {
                _logger.Error($"Unable to get latest symbol {e.Symbol} price.");
                return;
            }

            TradeConfig symbolTradeConfig = _config.TradeConfigs.First(x => x.Symbol == e.Symbol);

            if (GetTradesFromBuffer(e.Symbol, out List<ITrade> trades))
            {
                var activeTrades = trades.Where(x => x.IsActive);

                if (!activeTrades.IsNullOrEmpty())
                {
                    if (e.MarketDirection == MarketDirection.Buy)
                    {
                        if (activeTrades.Where(x => x.MarketDirection == MarketDirection.Buy).Count() >= symbolTradeConfig.ConcurrentTradesPerDirection)
                            return;
                    }
                    else if (e.MarketDirection == MarketDirection.Sell)
                    {
                        if (activeTrades.Where(x => x.MarketDirection == MarketDirection.Sell).Count() >= symbolTradeConfig.ConcurrentTradesPerDirection)
                            return;
                    }
                }
            }

            TradeArgs tradeArgs = new TradeArgs(e.Symbol);
            tradeArgs.MarketDirection = e.MarketDirection;
            tradeArgs.MarketEvaluation = e.MarketEvaluation;
            tradeArgs.Price = price.Value;

            if (e.MarketDirection == MarketDirection.Buy)
            {
                tradeArgs.TakeProfitPrice = price.Value + symbolTradeConfig.TakeProfit;
                tradeArgs.StopLossPrice = price.Value - symbolTradeConfig.StopLoss;
            }
            else if (e.MarketDirection == MarketDirection.Sell)
            {
                tradeArgs.TakeProfitPrice = price.Value - symbolTradeConfig.TakeProfit;
                tradeArgs.StopLossPrice = price.Value + symbolTradeConfig.StopLoss;
            }

            if (CreateTrade(tradeArgs, out ITrade trade))
            {
                SaveTradeToFile(tradeArgs.Symbol, trade);

                SaveTradeToBuffer(tradeArgs.Symbol, trade);
            }
        }

        private bool GetTradesFromBuffer(string symbol, out List<ITrade> trades)
        {
            trades = null;

            lock (_tradesBuffer)
            {
                if (_tradesBuffer.TryGetValue(symbol, out List<ITrade> tb))
                {
                    trades = new List<ITrade>(tb);
                    return !trades.IsNullOrEmpty();
                }

                return false;
            }
        }

        private bool GetTradeFromBuffer(string symbol, string tradeId, out ITrade trade)
        {
            trade = null;

            lock (_tradesBuffer)
            {
                if (_tradesBuffer.TryGetValue(symbol, out List<ITrade> tb))
                {
                    trade = tb.FirstOrDefault(x => x.Id == tradeId);
                    return trade != null;
                }

                return false;
            }
        }

        private void SaveTradeToBuffer(string symbol, ITrade trade)
        {
            lock (_tradesBuffer)
            {
                if (!_tradesBuffer.TryGetValue(symbol, out _))
                {
                    _tradesBuffer.Add(symbol, new List<ITrade>() { trade });
                }
                else
                {
                    _tradesBuffer[symbol].Add(trade);
                }
            }
        }

        private void SaveTradeToFile(string symbol, ITrade trade)
        {
            if (trade == null)
            {
                _logger.Warn($"Failed to save trade to file. No {symbol} trade present.");
                return;
            }

            string filePath;
            if (!trade.ClosedAt.HasValue)
            {
                filePath = Path.Combine(_config.CreatedTradesDestinationPath, $"{symbol}_createdTrades_{DateTime.Now:ddMMyyyy}.txt");
            }
            else
            {
                filePath = Path.Combine(_config.ClosedTradesDestinationPath, $"{symbol}_closedTrades_{DateTime.Now:ddMMyyyy}.txt");
            }

            try
            {
                Helpers.WriteToFile(trade.Dump() + Environment.NewLine, filePath);
            }
            catch (Exception e)
            {
                _logger.Error($"Failed to save trade to file '{filePath}'. {e}");
            }
        }

        private void MonitorWalletBalanceInThread()
        {
            while(true)
            {
                _logger.Info($">>> SIMULATOR WALLET BALANCE: {GetWalletBalance()}$");

                Task.Delay(MONITOR_WALLET_BALANCE_DELAY).Wait();
            }
        }
    }
}
