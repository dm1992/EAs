using Bybit.Net;
using Bybit.Net.Clients;
using CryptoExchange.Net.Authentication;
using System;

namespace MarketProxy.Api.BybitApi
{
    public abstract class BybitBaseApi
    {
        protected readonly BybitRestClient _client;

        public BybitBaseApi(string apiKey = null, string apiSecret = null, bool liveEnvironment = true)
        {
            _client = SetupClient(apiKey, apiSecret, liveEnvironment);
        }

        private BybitRestClient SetupClient(string apiKey = null, string apiSecret = null, bool liveEnvironment = true)
        {
            return new BybitRestClient(optionsDelegate =>
            {
                optionsDelegate.Environment = liveEnvironment ? BybitEnvironment.Live : BybitEnvironment.Testnet;

                if (!String.IsNullOrEmpty(apiKey))
                {
                    if (!String.IsNullOrEmpty(apiSecret))
                    {
                        optionsDelegate.ApiCredentials = new ApiCredentials(apiKey, apiSecret);
                    }
                }
            });
        }
    }
}
