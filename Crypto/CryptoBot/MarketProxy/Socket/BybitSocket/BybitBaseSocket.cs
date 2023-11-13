using Bybit.Net;
using Bybit.Net.Clients;
using CryptoExchange.Net.Authentication;
using MarketProxy.Events;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MarketProxy.Socket.BybitSocket
{
    public abstract class BybitBaseSocket
    {
        protected BybitSocketClient _socket;

        public BybitBaseSocket(string apiKey = null, string apiSecret = null, bool liveEnvironment = true)
        {
            _socket = SetupSocket(apiKey, apiSecret, liveEnvironment);
        }

        private BybitSocketClient SetupSocket(string apiKey = null, string apiSecret = null, bool liveEnvironment = true)
        {
            return new BybitSocketClient(optionsDelegate =>
            {
                optionsDelegate.Environment = liveEnvironment ? BybitEnvironment.Live : BybitEnvironment.Testnet;
                optionsDelegate.AutoReconnect = true;

                if (!String.IsNullOrEmpty(apiKey))
                {
                    if (!String.IsNullOrEmpty(apiSecret))
                    {
                        // set api credentials
                        optionsDelegate.ApiCredentials = new ApiCredentials(apiKey, apiSecret);
                    }
                }
            });
        }
    }
}
