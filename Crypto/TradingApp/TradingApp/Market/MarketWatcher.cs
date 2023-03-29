using Bybit.Net.Clients;
using Bybit.Net.Objects.Models.Socket.Spot;
using CryptoExchange.Net.Sockets;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TradingApp.Data;
using TradingApp.EventArgs;
using TradingApp.Interfaces;

namespace TradingApp.Market
{
    public class MarketWatcher : IApplicationEvent
    {
        private readonly BybitSocketClient _bybitSocketClient;
        private readonly Config _config;

        private List<DataEvent<BybitSpotTradeUpdate>> _tradeHistory;
        private Dictionary<string, DateTime?> _symbolTimes;

        public event EventHandler<MarketWatcherEventArgs> MarketWatcherEvent;
        public event EventHandler<ApplicationEventArgs> ApplicationEvent;

        public MarketWatcher(Config config)
        {
            var options = Bybit.Net.Objects.BybitSocketClientOptions.Default;
            options.OutputOriginalData = true;
            _bybitSocketClient = new BybitSocketClient(options);

            _tradeHistory = new List<DataEvent<BybitSpotTradeUpdate>>();
            _symbolTimes = new Dictionary<string, DateTime?>();
            _config = config;

            SubscribeToTradeUpdatesAsync();
        }

        public async void SubscribeToTradeUpdatesAsync()
        {
            foreach (string symbol in _config.Symbols)
            {
                _symbolTimes.Add(symbol, null);

                await _bybitSocketClient.SpotStreams.SubscribeToTradeUpdatesAsync(symbol, ExecutedTradeReceiver);
            }

            ApplicationEvent?.Invoke(this, new ApplicationEventArgs(EventType.INFORMATION, $"Bybit registered symbols ({String.Join(",", _config.Symbols)})"));
        }

        private void ExecutedTradeReceiver(DataEvent<BybitSpotTradeUpdate> trade)
        {
            lock (this)
            {
                try
                {
                    if (_symbolTimes[trade.Topic] == null)
                    {
                        _symbolTimes[trade.Topic] = DateTime.Now.AddSeconds(-DateTime.Now.Second);

                        ApplicationEvent?.Invoke(this, new ApplicationEventArgs(EventType.INFORMATION, 
                                         $"Started new candle for symbol {trade.Topic} at {_symbolTimes[trade.Topic]}."));
                    }

                    if ((DateTime.Now - _symbolTimes[trade.Topic].Value).TotalMinutes >= _config.CandleTimeframe)
                    {
                        ApplicationEvent?.Invoke(this, new ApplicationEventArgs(EventType.INFORMATION,
                                        $"Finished candle for symbol {trade.Topic} at {DateTime.Now}."));

                        var symbolTradeHistory = _tradeHistory.Where(x => x.Topic == trade.Topic).OrderBy(x => x.Timestamp);
                        var candle = GetFinishedCandle(symbolTradeHistory);

                        MarketWatcherEvent?.Invoke(this, new MarketWatcherEventArgs(candle));

                        SaveCandleRawTrades(candle, symbolTradeHistory);

                        _tradeHistory.RemoveAll(x => x.Topic == trade.Topic);
                        _symbolTimes[trade.Topic] = null;
                    }

                    _tradeHistory.Add(trade);
                }
                catch (Exception e)
                {
                    ApplicationEvent?.Invoke(this, new ApplicationEventArgs(EventType.ERROR, e.Message));
                }
            }
        }

        private Candle GetFinishedCandle(IEnumerable<DataEvent<BybitSpotTradeUpdate>> tradeHistory)
        {
            var tradeHistoryInstance = tradeHistory.First();

            var c = new Candle();
            c.Time = _symbolTimes[tradeHistoryInstance.Topic].Value;
            c.Symbol = tradeHistoryInstance.Topic;
            c.PriceOpen = tradeHistoryInstance.Data.Price;
            c.PriceHigh = tradeHistory.Max(x => x.Data.Price);
            c.PriceLow = tradeHistory.Min(x => x.Data.Price);
            c.PriceClose = tradeHistory.Last().Data.Price;
            c.Buyers = tradeHistory.Where(x => x.Data.Buy).Sum(x => x.Data.Quantity);
            c.Sellers = tradeHistory.Where(x => !x.Data.Buy).Sum(x => x.Data.Quantity);

            if (c.PriceOpen < c.PriceClose)
            {
                c.StrengthBuyers = c.Buyers == 0 ? 0 : Math.Abs(c.PriceHigh - c.PriceLow) / c.Buyers;
                c.StrengthSellers = c.Sellers == 0 ? 0 : Math.Abs(c.PriceHigh - c.PriceClose + c.PriceOpen - c.PriceLow) / c.Sellers;
            }
            else if (c.PriceOpen > c.PriceClose)
            {
                c.StrengthBuyers = c.Buyers == 0 ? 0 : Math.Abs(c.PriceHigh - c.PriceOpen + c.PriceClose - c.PriceLow) / c.Buyers;
                c.StrengthSellers = c.Sellers == 0 ? 0 : Math.Abs(c.PriceHigh - c.PriceLow) / c.Sellers;
            }
            else
            {
                c.StrengthBuyers = c.Buyers == 0 ? 0 : Math.Abs(c.PriceHigh - c.PriceLow) / c.Buyers;
                c.StrengthSellers = c.Sellers == 0 ? 0 : Math.Abs(c.PriceHigh - c.PriceLow) / c.Sellers;
            }

            return c;
        }

        private void SaveCandleRawTrades(Candle candle, IEnumerable<DataEvent<BybitSpotTradeUpdate>> tradeHistory)
        {
            if (!Helpers.SaveData($"{candle.Dump()}\n{tradeHistory.GetStringFromCollection()}", Path.Combine(_config.MarketRawDataDirectory, $"{candle.Symbol}_rawData_{DateTime.Now:ddMMyyyy}.txt"), out string errorReason))
            {
                // not critical only report via event
                ApplicationEvent?.Invoke(this, new ApplicationEventArgs(EventType.WARNING, $"Failed to save raw trades on finished candle for symbol {candle.Symbol}. Reason: {errorReason}"));
                return;
            }

            ApplicationEvent?.Invoke(this, new ApplicationEventArgs(EventType.INFORMATION, $"Raw trades on finished candle {candle.Symbol} saved."));
        }
    }
}
