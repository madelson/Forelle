using Medallion.Collections;
using System;
using System.Collections.Generic;
using System.Text;
using System.Linq;

namespace Forelle
{
    internal static class Helpers
    {
        public static TResult Only<TSource, TResult>(this IEnumerable<TSource> source, Func<TSource, TResult> selector, IEqualityComparer<TResult> comparer = null)
        {
            if (source == null) { throw new ArgumentNullException(nameof(source)); }
            if (selector == null) { throw new ArgumentNullException(nameof(selector)); }

            var comparerToUse = comparer ?? EqualityComparer<TResult>.Default;
            using (var enumerator = source.GetEnumerator())
            {
                if (!enumerator.MoveNext()) { throw new InvalidOperationException("The sequence contained no elements"); }

                var value = selector(enumerator.Current);

                while (enumerator.MoveNext())
                {
                    var otherValue = selector(enumerator.Current);
                    if (!comparerToUse.Equals(value, otherValue))
                    {
                        throw new InvalidOperationException($"The sequence contained multiple values: '{value}', '{otherValue}'");
                    }
                }

                return value;
            }
        }

        public static T As<T>(this T @this) => @this;

        public static void Deconstruct<TKey, TValue>(this KeyValuePair<TKey, TValue> keyValuePair, out TKey key, out TValue value)
        {
            key = keyValuePair.Key;
            value = keyValuePair.Value;
        }
    }
}
