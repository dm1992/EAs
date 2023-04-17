﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CryptoBot.Interfaces.Events
{
    public interface IWebSocketEvent : IApplicationEvent
    {
        void InvokeWebSocketEventSubscription();
        void CloseWebSocketEventSubscription();
    }
}
