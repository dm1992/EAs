using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace TradeCollectorApp
{
    /// <summary>
    /// Common helper methods.
    /// </summary>
    public static class Helpers
    {
        private static object _locker = new object();

        public static JsonSerializerOptions GetDefaultJsonSerializerOptions()
        {
            JsonSerializerOptions options = new JsonSerializerOptions();
            options.WriteIndented = true;
            options.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
            options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, true));
            options.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;

            return options;
        }

        public static bool SaveData(string data, string fp, out string errorReason)
        {
            errorReason = null;

            lock (_locker)
            {
                try
                {
                    string dirPath = Path.GetDirectoryName(fp);
                    if (!Directory.Exists(dirPath)) Directory.CreateDirectory(dirPath);

                    string currentContent = "";
                    if (File.Exists(fp))
                    {
                        currentContent = File.ReadAllText(fp);
                    }

                    File.WriteAllText(fp, data + currentContent);
                }
                catch (Exception e)
                {
                    errorReason = e.Message;
                }

                return errorReason == null;
            }
        }

        public static string StringBuilder<T>(this IEnumerable<T> items)
        {
            lock (_locker)
            {
                string data = "";

                foreach (var item in new List<T>(items))
                {
                    data += item.ToString();
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
