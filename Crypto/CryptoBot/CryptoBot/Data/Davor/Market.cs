using Bybit.Net.Objects.Models.Socket.Spot;
using Bybit.Net.Objects.Models.Spot;
using CryptoBot.Interfaces;
using CryptoExchange.Net.Sockets;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CryptoBot.Data.Davor
{
    public class Market : IMarket
    {
        public string Id { get; private set; }
        public string Symbol { get; set; }
        public DateTime CreatedAt { get; set; }
        public AggressiveMarket AggressiveMarket { get; set; }
        public PassiveMarket PassiveMarket { get; set; }
        public decimal VolumePercentage { get; set; }

        public decimal AverageMinuteBuyersVolume { get; set; }
        public decimal AverageMinuteSellersVolume { get; set; }
        public decimal AverageMinutePriceMovePercentage { get; set; }

        public Market(string symbol)
        {
            this.Symbol = symbol;
            this.Id = Guid.NewGuid().ToString();
            this.CreatedAt = DateTime.Now;
        }

        public Market(string symbol, AggressiveMarket aggressiveMarket, PassiveMarket passiveMarket, decimal volumePercentage)
        {
            this.Symbol = symbol;
            this.Id = Guid.NewGuid().ToString();
            this.AggressiveMarket = aggressiveMarket;
            this.PassiveMarket = passiveMarket;
            this.VolumePercentage = volumePercentage;
            this.CreatedAt = DateTime.Now;
        }

        public MarketDirection GetMarketDirection()
        {
            if (this.AggressiveMarket == null || this.PassiveMarket == null)
                return MarketDirection.Unknown;

            MarketDirection marketDirection = MarketDirection.Unknown;
            MarketDirection aggressiveMarketDirection = this.AggressiveMarket.GetMarketDirection();
            MarketDirection passiveMarketDirection = this.PassiveMarket.GetMarketDirection();

            if (aggressiveMarketDirection == MarketDirection.Buy)
            {
                if (passiveMarketDirection == MarketDirection.Buy)
                {
                    //if (this.AggressiveMarket.BuyersVolume > this.PassiveMarket.SellersVolume)
                    //{
                    //    if (100 - (this.PassiveMarket.SellersVolume / this.AggressiveMarket.BuyersVolume) * 100 >= this.VolumePercentage)
                    //    {
                            marketDirection = MarketDirection.Buy;
                    //    }
                    //}
                }
            }
            else if (aggressiveMarketDirection == MarketDirection.Sell)
            {
                if (passiveMarketDirection == MarketDirection.Sell)
                {
                    //if (this.AggressiveMarket.SellersVolume > this.PassiveMarket.BuyersVolume)
                    //{
                    //    if (100 - (this.PassiveMarket.BuyersVolume / this.AggressiveMarket.SellersVolume) * 100 >= this.VolumePercentage)
                    //    {
                            marketDirection = MarketDirection.Sell;
                    //    }
                    //}
                }
            }

            return marketDirection;
        }

        public string Dump()
        {
            return $"\n--- {this.Symbol} MARKET ---\n" +
                   $"AverageMinuteBuyersVolume: {this.AverageMinuteBuyersVolume},\n" +
                   $"AverageMinuteSellersVolume: {this.AverageMinuteSellersVolume},\n" +
                   $"AverageMinutePriceMovePercentage: {this.AverageMinutePriceMovePercentage},\n" +
                   $"------------------------\n";
        }
    }

    public class AggressiveMarket : IAggressiveMarket
    {
        public string Id { get; private set; }
        public string Symbol { get; set; }
        public DateTime CreatedAt { get; set; }
        public List<DataEvent<BybitSpotTradeUpdate>> Trades { get; set; }
        public decimal VolumePercentage { get; set; }
        public decimal BuyersVolume
        {
            get
            {
                var buyerTrades = this.Trades.Where(x => x.Data.Buy);
                if (buyerTrades.IsNullOrEmpty())
                    return 0;

                return buyerTrades.Sum(x => x.Data.Quantity);
            }
        }
        public decimal SellersVolume
        {
            get
            {
                var sellerTrades = this.Trades.Where(x => !x.Data.Buy);
                if (sellerTrades.IsNullOrEmpty())
                    return 0;

                return sellerTrades.Sum(x => x.Data.Quantity);
            }
        }      
        public decimal LatestPrice
        {
            get
            {
                if (this.Trades.IsNullOrEmpty())
                    return 0;

                return this.Trades.OrderByDescending(x => x.Data.Timestamp).FirstOrDefault()?.Data.Price ?? 0;
            }
        }

        public decimal AverageMinuteBuyersVolume => throw new NotImplementedException();

        public decimal AverageMinuteSellersVolume => throw new NotImplementedException();

        public decimal AverageMinutePriceMovePercentage => throw new NotImplementedException();

        public AggressiveMarket(string symbol, List<DataEvent<BybitSpotTradeUpdate>> trades, decimal volumePercentage)
        {
            this.Id = Guid.NewGuid().ToString();
            this.Symbol = symbol;
            this.Trades = trades;
            this.VolumePercentage = volumePercentage;
            this.CreatedAt = DateTime.Now;
        }

        public MarketDirection GetMarketDirection()
        {
            MarketDirection marketDirection = MarketDirection.Unknown;

            if (this.BuyersVolume > this.SellersVolume)
            {
                if (100 - (this.SellersVolume / this.BuyersVolume) * 100 >= this.VolumePercentage)
                {
                    marketDirection = MarketDirection.Buy;
                }
            }
            else if (this.SellersVolume > this.BuyersVolume)
            {
                if (100 - (this.BuyersVolume / this.SellersVolume) * 100 >= this.VolumePercentage)
                {
                    marketDirection = MarketDirection.Sell;
                }
            }

            return marketDirection;
        }

        public string DumpTrades()
        {
            List<string> tradeInfos = new List<string>();
            tradeInfos.Add("\n--------------------");

            foreach (var trade in this.Trades.OrderBy(x => x.Timestamp))
            {
                tradeInfos.Add($"{trade.Timestamp}, {(trade.Data.Buy ? "BUY" : "SELL")}, {trade.Data.Price}, {trade.Data.Quantity}");
            }

            return String.Join("\n", tradeInfos);
        }

        public string Dump()
        {
            return $"\n--- {this.Symbol} AGRESSIVE MARKET ---\n" +
                   $"Id: {this.Id},\n" +
                   $"------------------------\n" +
                   $"BuyersVolume: {this.BuyersVolume},\n" +
                   $"SellersVolume: {this.SellersVolume},\n" +
                   $"LatestPrice: {this.LatestPrice},\n" +
                   $"------------------------\n" +
                   $"MarketDirection: {this.GetMarketDirection()}\n";
        }
    }

    public class PassiveMarket : IPassiveMarket
    {
        public string Id { get; private set; }
        public string Symbol { get; set; }
        public DateTime CreatedAt { get; set; }
        public IEnumerable<BybitSpotOrderBookEntry> Bids { get; set; }
        public IEnumerable<BybitSpotOrderBookEntry> Asks { get; set; }
        public decimal VolumePercentage { get; set; }
        public decimal BuyersVolume
        {
            get
            {
                if (this.Bids.IsNullOrEmpty())
                    return 0;

                return this.Bids.Sum(x => x.Quantity);
            }
        }
        public decimal SellersVolume
        {
            get
            {
                if (this.Asks.IsNullOrEmpty())
                    return 0;

                return this.Asks.Sum(x => x.Quantity);
            }
        }

        public decimal AverageMinuteBuyersVolume => throw new NotImplementedException();

        public decimal AverageMinuteSellersVolume => throw new NotImplementedException();

        public decimal AverageMinutePriceMovePercentage => throw new NotImplementedException();

        public PassiveMarket(string symbol, IEnumerable<BybitSpotOrderBookEntry> bids, IEnumerable<BybitSpotOrderBookEntry> asks, decimal volumePercentage)
        {
            this.Id = Guid.NewGuid().ToString();
            this.CreatedAt = DateTime.Now;
            this.Symbol = symbol;
            this.Bids = bids;
            this.Asks = asks;
            this.VolumePercentage = volumePercentage;
        }

        public MarketDirection GetMarketDirection()
        {
            MarketDirection marketDirection = MarketDirection.Unknown;

            if (this.BuyersVolume > this.SellersVolume)
            {
                if (100 - (this.SellersVolume / this.BuyersVolume) * 100 >= this.VolumePercentage)
                {
                    marketDirection = MarketDirection.Buy;
                }
            }
            else if (this.SellersVolume > this.BuyersVolume)
            {
                if (100 - (this.BuyersVolume / this.SellersVolume) * 100 >= this.VolumePercentage)
                {
                    marketDirection = MarketDirection.Sell;
                }
            }

            return marketDirection;
        }

        public string Dump()
        {
            throw new NotImplementedException();
        }
    }
}
