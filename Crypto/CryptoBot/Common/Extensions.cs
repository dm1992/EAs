using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Common
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
    }
}
