using Bybit.Net.Objects.Models.Derivatives;
using CryptoExchange.Net.Objects;
using MarketClient.Interfaces.Api;
using MarketProxy.Api.BybitApi;
using NLog;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace MarketProxy.BybitApi
{
    public class BybitExchangeApi : BybitBaseApi, IExchangeApi
    {
        private readonly ILogger _logger;

        public BybitExchangeApi(LogFactory logFactory, string apiKey = null, string apiSecret = null, bool liveEnvironment = true) : base(apiKey, apiSecret, liveEnvironment)
        {
            _logger = logFactory.GetCurrentClassLogger();
        }

        public async Task<decimal?> GetPrice(string symbol)
        {
            var response = await _client.DerivativesApi.ExchangeData.GetTickerAsync(Bybit.Net.Enums.Category.Inverse, symbol);

            if (!response.GetResultOrError(out IEnumerable<BybitDerivativesTicker> data, out Error error))
            {
                _logger.Error($"Failed to get price. Error: ({error?.Code}) {error?.Message}.");
                return null;
            }

            return data.First().LastPrice;
        }
    }
}
