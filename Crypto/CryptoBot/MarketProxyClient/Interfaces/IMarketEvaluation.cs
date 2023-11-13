using Common;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MarketProxyClient.Interfaces
{
    public interface IMarketEvaluation
    {
        string Id { get; }
        DateTime CreatedAt { get; }
        string Symbol { get; set; }
        MarketEvaluationThresholdConfig MarketEvaluationThresholdConfig { get; set; }
        decimal BuyVolume { get; set; }
        decimal SellVolume { get; set; }
        decimal BuyVolumeVelocity { get; set; }
        decimal SellVolumeVelocity { get; set; }
        decimal BuyDeltaPrice { get; set; }
        decimal SellDeltaPrice { get; set; }
        decimal BuyVolumeDeltaPrice { get; set; }
        decimal SellVolumeDeltaPrice { get; set; }
        decimal BuyDeltaPriceVelocity { get; set; }
        decimal SellDeltaPriceVelocity { get; set; }
        ImpulseEffect ImpulseEffect { get; }
        WallEffect WallEffect { get; }

        string Dump();
    }
}
