using Common;
using Common.Events;
using MarketProxyClient.Interfaces;

namespace MarketProxyClient.Events
{
    public class MarketEvaluationEventArgs : BaseEventArgs
    {
        public string Symbol { get; set; }
        public IMarketEvaluation MarketEvaluation { get; set; }

        public MarketEvaluationEventArgs(string symbol, IMarketEvaluation marketEvaluation) : base(MessageType.Info, "New market evaluation created.")
        {
            this.Symbol = symbol;
            this.MarketEvaluation = marketEvaluation;
        }
    }
}
