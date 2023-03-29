using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CryptoBot
{
    public static class Extensions
    {
        public static bool IsNullOrEmpty<T>(this IEnumerable<T> list)
        {
            if (list == null || !list.Any())
                return true;

            return false;
        }

        public static IEnumerable<T> ParseCsv<T>(this string csv)
        {
            if (String.IsNullOrWhiteSpace(csv)) yield break;

            foreach (var d in csv.Split(','))
            {
                yield return (T)Convert.ChangeType(d.Trim(), typeof(T));
            }
        }

        public static object ParseObject(string objectRawData, string key)
        {
            try
            {
                if (String.IsNullOrEmpty(objectRawData) || String.IsNullOrEmpty(key))
                    return null;

                JObject @object = JObject.Parse(objectRawData);
                switch (key)
                {
                    case "topic":
                        string data = Convert.ToString(@object[key]);
                        return data.Replace("trade.", String.Empty);

                    default:
                        return null;
                }

            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
