using Bybit.Net.Clients;
using Bybit.Net.Objects.Models.Socket.Spot;
using Bybit.Net.Objects.Models.Spot;
using CryptoExchange.Net.Objects;
using CryptoExchange.Net.Sockets;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TradeCollectorApp.Models;

namespace TradeCollectorApp.Managers
{
    public class BybitAPIManager : IAPIManager
    {
        private readonly BybitClient _bybitClient;
        private readonly BybitSocketClient _bybitSocketClient;

        private int _collectionTimeout;
        private string _directoryPath;
        private List<string> _symbols;
        private Dictionary<CollectorMetadata, bool> _symbolCollectors;

        public bool TradeCollectFinished { get { return _symbolCollectors.Values.All(x => x == true); } }

        public BybitAPIManager()
        {
            _bybitClient = new BybitClient(Bybit.Net.Objects.BybitClientOptions.Default); //xxx add api tokens
            _bybitSocketClient = new BybitSocketClient(Bybit.Net.Objects.BybitSocketClientOptions.Default);

            _collectionTimeout = int.Parse(ConfigurationManager.AppSettings["tradeCollectionTimeout"]);
            _directoryPath = ConfigurationManager.AppSettings["testResultFilePath"];
            _symbols = ConfigurationManager.AppSettings["symbols"].ParseCsv().ToList();
            _symbolCollectors = new Dictionary<CollectorMetadata, bool>();

            SubscribeToTradeUpdatesAsync();
        }

        public async void SubscribeToTradeUpdatesAsync()
        {
            foreach (string symbol in _symbols)
            {
                CollectorMetadata metadata = new CollectorMetadata();
                metadata.Symbol = symbol;
                metadata.CollectionTimeout = _collectionTimeout;

                _symbolCollectors.Add(metadata, false);

                await _bybitSocketClient.SpotStreams.SubscribeToTradeUpdatesAsync(symbol, TradeCollectionHandler);
            }
        }

        private async void TradeCollectionHandler(DataEvent<BybitSpotTradeUpdate> tradeUpdate)
        {
            CollectorMetadata symbolCollector = _symbolCollectors.First(x => x.Key.Symbol == tradeUpdate.Topic).Key;

            if (symbolCollector.CollectionStartedAt == null)
            {
                Console.WriteLine("\n-----------------");
                Console.WriteLine($"--> BYBIT - ({tradeUpdate.Topic})\n" +
                                    $"ZAČETEK zbiranja trejdov ob: {DateTime.Now} (lokalni čas).\n" +
                                    $"KONEC zbiranja trejdov ob: {DateTime.Now.AddMilliseconds(symbolCollector.CollectionTimeout)} (lokalni čas).\n" +
                                    $"Zbrani trejdi shranjeni v: {Path.Combine(_directoryPath, "CollectedTrades", $"bybit_{tradeUpdate.Topic}.txt")}.");

                symbolCollector.CollectionStartedAt = DateTime.Now;
            }
            else if (symbolCollector.CollectionFinished)
            {
                if (_symbolCollectors[symbolCollector] == false)
                {
                    Console.WriteLine("\n-----------------\n");
                    Console.WriteLine($"<-- BYBIT - ({tradeUpdate.Topic})\n" +
                    $"KONEC zbiranja trejdov.");
                    Console.WriteLine("-----------------\n");

                    _symbolCollectors[symbolCollector] = true; // stop collecting trades on this symbol
                }

                return;
            }

            WebCallResult<BybitSpotOrderBook> orderBookData = await _bybitClient.SpotApi.ExchangeData.GetOrderBookAsync(tradeUpdate.Topic, 1);

            if (!orderBookData.Success)
            {
                Console.WriteLine($"Napaka pri pridobivanju orderbook podatkov za valuto {tradeUpdate.Topic} ob: {DateTime.Now} (lokalni čas).");
                return;
            }

            lock (this)
            {
                Trade trade = new Trade();
                trade.Id = tradeUpdate.Data.Id;
                trade.Symbol = tradeUpdate.Topic;
                trade.Price = tradeUpdate.Data.Price;
                trade.Quantity = tradeUpdate.Data.Quantity;
                trade.Timestamp = tradeUpdate.Data.Timestamp;
                trade.Type = tradeUpdate.Data.Buy ? TradeType.BUY : TradeType.SELL;

                var bid = orderBookData.Data.Bids.First();
                trade.BidPrice = bid.Price;
                trade.BidQuantity = bid.Quantity;

                var ask = orderBookData.Data.Asks.First();
                trade.AskPrice = ask.Price;
                trade.AskQuantity = ask.Quantity;

                symbolCollector.TradeBuffer.Add(trade);

                var symbolTrades = symbolCollector.TradeBuffer.OrderByDescending(x => x.Id); // newest trades first

                if (!Helpers.SaveData(symbolTrades.StringBuilder(), Path.Combine(_directoryPath, "CollectedTrades", $"bybit_{tradeUpdate.Topic}.txt"), out string errorReason))
                {
                    Console.WriteLine($"Napaka pri shranjevanju {tradeUpdate.Topic} trejdov z Id [{String.Join(", ", symbolTrades.Select(x => x.Id))}].\n" +
                                        $"Trejdi se bodo shranili ob prvem uspelem poskusu shranjevanja.\n" +
                                        $"Vzrok napake: {errorReason}.");
                    return;
                }

                symbolCollector.TradeBuffer.Clear(); // flush symbol trade buffer
            }
        }
    }
}
