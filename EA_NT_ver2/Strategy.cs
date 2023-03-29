using EA.Data;
using Newtonsoft.Json;
using NQuotes;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;

namespace EA
{
    public partial class Strategy : MqlApi
    {
        public override int init()
        {
            _settings = new Settings();
            _tradingSymbols = new List<TradingSymbol>();
            _placedOrders = new List<Order>();

            foreach (string tradingSymbolName in _settings.TradingSymbolNames)
            {
                TradingSymbol ts = new TradingSymbol(tradingSymbolName);
                ts.PrepareTimeIntervals();
                _tradingSymbols.Add(ts);

                Dump(ts.TimeIntervals);
            }

            return 0;
        }

        public override int deinit()
        {
            return 0;
        }

        public override int start()
        {
            StartTrading();
            return 0;
        }
    }
}