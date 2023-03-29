using Bybit.Net.Objects.Models.Socket.Spot;
using CryptoExchange.Net.Sockets;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TradingApp.Data;

namespace TradingApp
{
    /// <summary>
    /// Common helper methods.
    /// </summary>
    public static class Helpers
    {
        private static object _locker = new object();

        public static bool SaveData(string data, string fp, out string errorReason)
        {
            errorReason = null;

            lock (_locker)
            {
                try
                {
                    string dirPath = Path.GetDirectoryName(fp);
                    if (!Directory.Exists(dirPath)) Directory.CreateDirectory(dirPath);

                    string currentContent = String.Empty;
                    if (File.Exists(fp)) currentContent = File.ReadAllText(fp);

                    File.WriteAllText(fp, data + currentContent);
                }
                catch (Exception e)
                {
                    errorReason = e.Message;
                }

                return errorReason == null;
            }
        }

        public static string GetStringFromCollection(this IEnumerable<DataEvent<BybitSpotTradeUpdate>> items)
        {
            lock (_locker)
            {
                string data = String.Empty;

                foreach (var item in items)
                {
                    data += $"{item.OriginalData}\n";
                }

                return data;
            }
        }

        public static IEnumerable<string> ParseCsv(this string csv)
        {
            if (String.IsNullOrWhiteSpace(csv)) yield break;

            foreach (string d in csv.Split(','))
            {
                yield return d.Trim();
            }
        }
    }
}
