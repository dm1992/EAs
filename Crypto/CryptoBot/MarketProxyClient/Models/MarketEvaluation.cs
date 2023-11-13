using MarketProxyClient.Interfaces;
using MarketProxy;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Common;

namespace MarketProxyClient.Models
{
    public class MarketEvaluation : IMarketEvaluation
    {
        public MarketEvaluation()
        {
            this.Id = Guid.NewGuid().ToString();
            this.CreatedAt = DateTime.UtcNow;
        }

        public string Id { get; }

        public DateTime CreatedAt { get; }

        public string Symbol { get; set; }

        public MarketEvaluationThresholdConfig MarketEvaluationThresholdConfig { get; set; }

        public List<MarketProxy.Models.Trade> Trades { get; set; }

        public MarketProxy.Models.Trade LatestTrade
        {
            get
            {
                if (this.Trades.IsNullOrEmpty())
                    return null;

                return this.Trades.Last();
            }
        }

        public decimal BuyVolume
        {
            get
            {
                if (this.Trades.IsNullOrEmpty())
                    return 0;

                return this.Trades.Where(x => x.TradeDirection == TradeDirection.Buy).Sum(x => x.Volume);
            }
            set 
            { 
                this.BuyVolume = value; 
            }
        }

        public decimal SellVolume
        {
            get
            {
                if (this.Trades.IsNullOrEmpty())
                    return 0;

                return this.Trades.Where(x => x.TradeDirection == TradeDirection.Sell).Sum(x => x.Volume);
            }
            set
            {
                this.SellVolume = value;
            }
        }

        public decimal BuyVolumeVelocity
        {
            get
            {
                if (this.Trades.IsNullOrEmpty())
                    return 0;

                decimal tradesTimeDiffInSeconds = (decimal)((this.Trades.Last().Time - this.Trades.First().Time).TotalSeconds);

                if (tradesTimeDiffInSeconds == 0)
                {
                    // 1 second by default to prevent divide by zero.
                    tradesTimeDiffInSeconds = 1;
                }

                return this.BuyVolume / tradesTimeDiffInSeconds;
            }
            set
            {
                this.BuyVolumeVelocity = value;
            }
        }

        public decimal SellVolumeVelocity
        {
            get
            {
                if (this.Trades.IsNullOrEmpty())
                    return 0;

                decimal tradesTimeDiffInSeconds = (decimal)((this.Trades.Last().Time - this.Trades.First().Time).TotalSeconds);

                if (tradesTimeDiffInSeconds == 0)
                {
                    // 1 second by default to prevent divide by zero.
                    tradesTimeDiffInSeconds = 1;
                }

                return this.SellVolume / tradesTimeDiffInSeconds;
            }
            set
            {
                this.SellVolumeVelocity = value;
            }
        }

        public decimal BuyDeltaPrice
        {
            get
            {
                if (this.Trades.IsNullOrEmpty())
                    return 0;

                return this.Trades.Where(x => x.TradeDirection == TradeDirection.Buy).Sum(x => x.DeltaPrice);
            }
            set
            {
                this.BuyDeltaPrice = value;
            }
        }

        public decimal SellDeltaPrice
        {
            get
            {
                if (this.Trades.IsNullOrEmpty())
                    return 0;

                return this.Trades.Where(x => x.TradeDirection == TradeDirection.Sell).Sum(x => x.DeltaPrice);
            }
            set
            {
                this.SellDeltaPrice = value;
            }
        }

        public decimal BuyVolumeDeltaPrice
        {
            get
            {
                return this.BuyVolume / (this.BuyDeltaPrice + 0.01m);
            }
            set
            {
                this.BuyVolumeDeltaPrice = value;
            }
        }

        public decimal SellVolumeDeltaPrice
        {
            get
            {
                return this.SellVolume / (this.SellDeltaPrice - 0.01m);
            }
            set
            {
                this.SellVolumeDeltaPrice = value;
            }
        }

        public decimal BuyDeltaPriceVelocity
        {
            get
            {
                if (this.Trades.IsNullOrEmpty())
                    return 0;

                decimal tradesTimeDiffInSeconds = (decimal)((this.Trades.Last().Time - this.Trades.First().Time).TotalSeconds);

                if (tradesTimeDiffInSeconds == 0)
                {
                    // 1 second by default to prevent divide by zero.
                    tradesTimeDiffInSeconds = 1;
                }

                return (this.BuyDeltaPrice + 0.01m) / tradesTimeDiffInSeconds;
            }
            set
            {
                this.BuyDeltaPriceVelocity = value;
            }
        }

        public decimal SellDeltaPriceVelocity
        {
            get
            {
                if (this.Trades.IsNullOrEmpty())
                    return 0;

                decimal tradesTimeDiffInSeconds = (decimal)((this.Trades.Last().Time - this.Trades.First().Time).TotalSeconds);

                if (tradesTimeDiffInSeconds == 0)
                {
                    // 1 second by default to prevent divide by zero.
                    tradesTimeDiffInSeconds = 1;
                }

                return (this.SellDeltaPrice + 0.01m) / tradesTimeDiffInSeconds;
            }
            set
            {
                this.SellDeltaPriceVelocity = value;
            }
        }

        public WallEffect WallEffect
        {
            get
            {
                if (this.MarketEvaluationThresholdConfig == null)
                    return WallEffect.Wait;

                if (this.BuyVolume > this.MarketEvaluationThresholdConfig.WallVolumeThreshold)
                {
                    if (this.BuyVolumeDeltaPrice > this.MarketEvaluationThresholdConfig.VolumeDeltaPriceThreshold)
                    {
                        return WallEffect.Sell;
                    }
                }
                else if (this.SellVolume > this.MarketEvaluationThresholdConfig.WallVolumeThreshold)
                {
                    if (this.SellVolumeDeltaPrice < -this.MarketEvaluationThresholdConfig.VolumeDeltaPriceThreshold)
                    {
                        return WallEffect.Buy;
                    }
                }

                return WallEffect.Wait;
            }
        }

        public ImpulseEffect ImpulseEffect
        {
            get
            {
                if (this.MarketEvaluationThresholdConfig == null)
                    return ImpulseEffect.Wait;

                if (this.BuyVolume > this.MarketEvaluationThresholdConfig.ImpulseVolumeThreshold)
                {
                    if (this.BuyDeltaPriceVelocity > this.MarketEvaluationThresholdConfig.DeltaPriceVelocityThreshold)
                    {
                        return ImpulseEffect.Sell;
                    }
                }
                else if (this.SellVolume > this.MarketEvaluationThresholdConfig.ImpulseVolumeThreshold)
                {
                    if (this.SellDeltaPriceVelocity < -this.MarketEvaluationThresholdConfig.DeltaPriceVelocityThreshold)
                    {
                        return ImpulseEffect.Buy;
                    }
                }

                return ImpulseEffect.Wait;
            }
        }

        public string Dump()
        {
            return  $"{this.Id};{this.CreatedAt};{this.LatestTrade.Dump(minimize: true)};{this.BuyVolume};{this.SellVolume};{this.BuyVolumeVelocity};{this.SellVolumeVelocity};{this.BuyDeltaPrice};{this.SellDeltaPrice};" +
                    $"{this.BuyVolumeDeltaPrice};{this.SellVolumeDeltaPrice};{this.BuyDeltaPriceVelocity};{this.SellDeltaPriceVelocity};{this.WallEffect};{this.ImpulseEffect}";
        }
    }
}
