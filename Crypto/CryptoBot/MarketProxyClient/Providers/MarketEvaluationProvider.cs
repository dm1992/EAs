using MarketProxyClient.Events;
using MarketProxyClient.Interfaces;
using MarketProxyClient.Models;
using MarketProxy.Events;
using NLog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Common;
using MarketClient.Interfaces.Socket;
using MarketProxy.Models;

namespace MarketProxyClient.Providers
{
    public class MarketEvaluationProvider : IMarketEvaluationProvider
    {
        private readonly ITradeSocket _tradeSocket;
        private readonly Config _config;
        private readonly ILogger _logger;
        private readonly SemaphoreSlim _tradeReceiveSemaphore;

        private bool _isInitialized;
        private Dictionary<string, List<MarketProxy.Models.Trade>> _tradeBuffer;

        public event EventHandler<MarketEvaluationEventArgs> MarketEvaluationEventHandler;

        public MarketEvaluationProvider(ITradeSocket tradeSocket, Config config, LogFactory logFactory)
        {
            _tradeSocket = tradeSocket;
            _config = config;
            _logger = logFactory.GetCurrentClassLogger();

            _isInitialized = false;
            _tradeReceiveSemaphore = new SemaphoreSlim(1, 1);

            _tradeBuffer = new Dictionary<string, List<MarketProxy.Models.Trade>>();
        }

        public bool Initialize()
        {
            if (_isInitialized)
                return true;

            _logger.Info("Initializing.");

            SubscribeToTrades();

            //SubscribeToOrderbook();

            _logger.Info("Initialized.");
            return _isInitialized = true;
        }

        private void SubscribeToTrades()
        {
            if (_tradeSocket != null)
            {
                _tradeSocket.TradeEventHandler += TradeReceiveEventHandler;
                _tradeSocket.UnsolicitedEventHandler += UnsolicitedEventHandler;

                _tradeSocket.SubscribeToTradesAsync(_config.Symbols);
            }
        }

        //private void SubscribeToOrderbook()
        //{
        //    if (_tradeSocket != null)
        //    {
        //        _tradeSocket.OrderbookEventHandler += OrderbookEventHandler;
        //        _tradeSocket.UnsolicitedEventHandler += UnsolicitedEventHandler;

        //        _tradeSocket.SubscribeToOrderbookAsync(_config.Symbols, depth: 50);
        //    }
        //}

        private void TradeReceiveEventHandler(object sender, TradeEventArgs e)
        {
            try
            {
                _tradeReceiveSemaphore.Wait();

                HandleTradeReceiveEvent(e);
            }
            finally
            {
                _tradeReceiveSemaphore.Release();
            }
        }

        private void UnsolicitedEventHandler(object sender, UnsolicitedEventArgs e)
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

        private void SaveReceivedTradesToFile(string symbol, List<MarketProxy.Models.Trade> trades)
        {
            if (trades.IsNullOrEmpty())
            {
                _logger.Warn($"Failed to save received trades to file. No {symbol} trades present.");
                return;
            }

            string filePath = Path.Combine(_config.ReceivedTradesDestinationPath, $"{symbol}_receivedTrades_{DateTime.Now:ddMMyyyy}.txt");

            try
            {
                string data = String.Join(Environment.NewLine, trades.OrderBy(x => x.Id).Select(x => x.Dump(minimize: true))) + Environment.NewLine;

                Helpers.WriteToFile(data, filePath);
            }
            catch (Exception e)
            {
                _logger.Error($"Failed to save received trades to file '{filePath}'. {e}");
            }
        }

        private bool GetTradesFromBuffer(string symbol, out List<MarketProxy.Models.Trade> trades)
        {
            if (_tradeBuffer.TryGetValue(symbol, out trades))
            {
                return !trades.IsNullOrEmpty();
            }

            return false;
        }

        private void DeleteTradesFromBuffer(string symbol, int? tradesToDelete = null)
        {
            if (!GetTradesFromBuffer(symbol, out List<MarketProxy.Models.Trade> trades))
            {
                _logger.Warn($"Failed to get {symbol} trades from buffer in order to delete them.");
                return;
            }

            if (!tradesToDelete.HasValue)
            {
                //_logger.Debug($"Deleting all {symbol} trades from buffer.");
                trades.Clear();
            }
            else if (tradesToDelete.Value > 0)
            {
                //_logger.Debug($"Deleting {tradesToDelete.Value} {symbol} trades from buffer.");
                trades.RemoveRange(0, tradesToDelete.Value);
            }
        }

        private void SaveTradesToBuffer(string symbol, List<MarketProxy.Models.Trade> trades)
        {
            if (trades.IsNullOrEmpty())
            {
                _logger.Warn($"Failed to save trades to buffer. No {symbol} trades present.");
                return;
            }

            if (!_tradeBuffer.TryGetValue(symbol, out _))
            {
                _tradeBuffer.Add(symbol, trades);
            }
            else
            {
                _tradeBuffer[symbol].AddRange(trades);
            }
        }

        private void SaveMarketEvaluationToFile(string symbol, IMarketEvaluation marketEvaluation)
        {
            if (marketEvaluation == null)
            {
                _logger.Warn($"Failed to save trade evaluation. No {symbol} trade evaluation present.");
                return;
            }

            string filePath = Path.Combine(_config.MarketEvaluationDestinationPath, $"{symbol}_marketEvaluation_{DateTime.Now:ddMMyyyy}.txt");

            try
            {
                string data = String.Join(Environment.NewLine, marketEvaluation.Dump()) + Environment.NewLine;

                Helpers.WriteToFile(data, filePath);
            }
            catch (Exception e)
            {
                _logger.Error($"Failed to save trade evaluation to file '{filePath}'. {e}");
            }
        }

        private void InvokeMarketEvaluationEvent(string symbol, IMarketEvaluation marketEvaluation)
        {
            if (MarketEvaluationEventHandler != null)
            {
                foreach (var attached in MarketEvaluationEventHandler.GetInvocationList())
                {
                    Task.Run(() =>
                    {
                        attached.DynamicInvoke(this, new MarketEvaluationEventArgs(symbol, marketEvaluation));
                    })
                    .ContinueWith(t =>
                    {
                        _logger.Error(t.Exception, "Failed in InvokeMarketEvaluationEvent.");
                    }, TaskContinuationOptions.OnlyOnFaulted);
                }
            }
        }

        private void PerformMarketEvaluation(string symbol)
        {
            if (!GetTradesFromBuffer(symbol, out List<MarketProxy.Models.Trade> trades))
            {
                _logger.Warn($"Failed to get {symbol} trades from buffer in order to perform market evaluation.");
                return;
            }

            int tradesOverflow = trades.Count - _config.ReceivedTradesThreshold;

            if (tradesOverflow <= 0)
            {
                //_logger.Debug($"Currently received {trades.Count} {symbol} trades didn't exceed received trades threshold set to {_config.ReceivedTradesThreshold} trades. Skipping market evaluation.");
                return;
            }

            for (int i = 0; i < tradesOverflow; i++)
            {
                MarketEvaluation marketEvaluation = new MarketEvaluation();
                marketEvaluation.Symbol = symbol;
                marketEvaluation.MarketEvaluationThresholdConfig = _config.MarketEvaluationThresholdConfigs.First(x => x.Symbol == symbol);
                marketEvaluation.Trades = trades.Skip(i).Take(_config.ReceivedTradesThreshold).ToList();

                SaveMarketEvaluationToFile(symbol, marketEvaluation);

                InvokeMarketEvaluationEvent(symbol, marketEvaluation);
            }

            DeleteTradesFromBuffer(symbol, tradesOverflow);
        }

        private void HandleTradeReceiveEvent(TradeEventArgs e)
        {
            if (e == null) return;

            SaveReceivedTradesToFile(e.Symbol, e.Trades);

            SaveTradesToBuffer(e.Symbol, e.Trades);

            PerformMarketEvaluation(e.Symbol);
        }
    }
}
