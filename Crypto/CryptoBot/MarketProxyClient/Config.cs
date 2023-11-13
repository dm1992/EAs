using MarketProxyClient.Interfaces;
using MarketProxyClient.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MarketProxyClient
{
    public class Config
    {
        public IEnumerable<string> Symbols { get; set; }
        public IEnumerable<MarketEvaluationThresholdConfig> MarketEvaluationThresholdConfigs { get; set; }
        public IEnumerable<TradeConfig> TradeConfigs { get; set; }
        public int ReceivedTradesThreshold { get; set; }
        public string MarketEvaluationDestinationPath { get; set; }
        public string ReceivedTradesDestinationPath { get; set; }
        public string CreatedTradesDestinationPath { get; set; }
        public string ClosedTradesDestinationPath { get; set; }


        public string Dump()
        {
            return "\n-------- CONFIG -----------\n" +
                   $"Symbols: {String.Join(",", this.Symbols)}\n" +
                   $"MarketEvaluationThresholdConfigs:\n" +
                   $"{String.Join("\n", this.MarketEvaluationThresholdConfigs.Select(x => x.Dump()))}\n" +
                   $"TradeConfigs:\n" +
                   $"{String.Join("\n", this.TradeConfigs.Select(x => x.Dump()))}\n" +
                   $"ReceivedTradesThreshold: {this.ReceivedTradesThreshold}\n" +
                   $"MarketEvaluationDestinationPath: {this.MarketEvaluationDestinationPath}\n" +
                   $"ReceivedTradesDestinationPath: {this.ReceivedTradesDestinationPath}\n" +
                   $"CreatedTradesDestinationPath: {this.CreatedTradesDestinationPath}\n" +
                   $"ClosedTradesDestinationPath: {this.ClosedTradesDestinationPath}\n" +
                   $"--------------------------\n";
        }
    }

    public class MarketEvaluationThresholdConfig
    {
        public string Symbol { get; set; }
        public decimal WallVolumeThreshold { get; set; }
        public decimal ImpulseVolumeThreshold { get; set; }
        public decimal VolumeDeltaPriceThreshold { get; set; }
        public decimal DeltaPriceVelocityThreshold { get; set; }

        public string Dump()
        {
            return $"Symbol: {this.Symbol}, WallVolumeThreshold: {this.WallVolumeThreshold}, ImpulseVolumeThreshold: {this.ImpulseVolumeThreshold}, " +
                   $"VolumeDeltaPriceThreshold: {this.VolumeDeltaPriceThreshold}, DeltaPriceVelocityThreshold: {this.DeltaPriceVelocityThreshold}";
        }
    }

    public class TradeConfig
    {
        public string Symbol { get; set; }
        public int ConcurrentTradesPerDirection { get; set; }
        public decimal TakeProfit { get; set; }
        public decimal StopLoss { get; set; }

        public string Dump()
        {
            return $"Symbol: {this.Symbol}, ConcurrentTradesPerDirection: {this.ConcurrentTradesPerDirection}, TakeProfit: {this.TakeProfit}, StopLoss: {this.StopLoss}";
        }

    }
}
