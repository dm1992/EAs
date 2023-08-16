using Bybit.Net.Objects.Models.V5;
using CryptoBot.Models;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

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

        public static string DictionaryToString<TKey, TValue>(this IDictionary<TKey, TValue> dictionary)
        {
            return "{" + string.Join(",", dictionary.Select(kv => kv.Key + "=" + kv.Value).ToArray()) + "}";
        }

        public static string ListToString<T>(this IList<T> list)
        {
            if (list.IsNullOrEmpty())
                return String.Empty;

            return "{" + string.Join(", ", list.ToArray()) + "}";
        }

        public static string ObjectToString(this object o)
        {
            string result = String.Empty;

            foreach (PropertyInfo prop in o.GetType().GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
            {
                result += $"{prop.Name} : {prop.GetValue(o, new object[] { })}\n";
            }

            return result;
        }
    }
}
