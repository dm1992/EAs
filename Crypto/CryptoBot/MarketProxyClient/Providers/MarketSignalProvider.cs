using Common;
using MarketProxyClient.Events;
using MarketProxyClient.Interfaces;
using MarketProxyClient.Models;
using NLog;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace MarketProxyClient.Providers
{
    public class MarketSignalProvider : IMarketSignalProvider
    {
        private readonly IMarketEvaluationProvider _marketEvaluationManager;
        private readonly Config _config;
        private readonly ILogger _logger;
        private readonly SemaphoreSlim _marketEvaluationSemaphore;

        private bool _isInitialized;

        public event EventHandler<MarketSignalEventArgs> MarketSignalEventHandler;

        public MarketSignalProvider(IMarketEvaluationProvider marketEvaluationManager, Config config, LogFactory logFactory)
        {
            _marketEvaluationManager = marketEvaluationManager;
            _config = config;
            _logger = logFactory.GetCurrentClassLogger();
            _marketEvaluationSemaphore = new SemaphoreSlim(1, 1);

            _isInitialized = false;
        }

        public bool Initialize()
        {
            if (_isInitialized)
                return true;

            _logger.Info("Initializing.");

            SubscribeToMarketEvaluation();

            //Task.Run(() => ProvideMarketInstructionInThread())
            //.ContinueWith(t =>
            //{
            //    _logger.Error(t.Exception, "Failed in ProvideMarketInstructionInThread.");
            //}, TaskContinuationOptions.OnlyOnFaulted);

            _logger.Info("Initialized.");
            return _isInitialized = true;
        }

        private void SubscribeToMarketEvaluation()
        {
            if (_marketEvaluationManager != null)
            {
                _logger.Info($"Subscribing to market evaluation.");

                _marketEvaluationManager.MarketEvaluationEventHandler += MarketEvaluationEventHandler;
            }
        }

        private void MarketEvaluationEventHandler(object sender, Events.MarketEvaluationEventArgs e)
        {
            try
            {
                _marketEvaluationSemaphore.Wait();

                HandleMarketEvaluationEvent(e);
            }
            finally
            {
                _marketEvaluationSemaphore.Release();
            }
        }

        private void HandleMarketEvaluationEvent(Events.MarketEvaluationEventArgs e)
        {
            if (e == null) return;

            if (e.MarketEvaluation.WallEffect == WallEffect.Buy)
            {
                //_logger.Info($"Detected BUY wall on symbol {e.Symbol}. Will invoke {e.Symbol} SELL market signal.");

                InvokeMarketSignalEvent(e.Symbol, MarketDirection.Buy, e.MarketEvaluation);
            }
            else if (e.MarketEvaluation.WallEffect == WallEffect.Sell)
            {
                //_logger.Info($"Detected SELL wall on symbol {e.Symbol}. Will invoke {e.Symbol} BUY market signal.");

                InvokeMarketSignalEvent(e.Symbol, MarketDirection.Sell, e.MarketEvaluation);
            }
        }

        private void InvokeMarketSignalEvent(string symbol, MarketDirection marketDirection, IMarketEvaluation marketEvaluation)
        {
            if (MarketSignalEventHandler != null)
            {
                foreach (var attached in MarketSignalEventHandler.GetInvocationList())
                {
                    Task.Run(() =>
                    {
                        attached.DynamicInvoke(this, new MarketSignalEventArgs(symbol, marketDirection, marketEvaluation));
                    })
                    .ContinueWith(t =>
                    {
                        _logger.Error(t.Exception, "Failed in InvokeMarketSignalEvent.");
                    }, TaskContinuationOptions.OnlyOnFaulted);
                }
            }
        }
    }
}
