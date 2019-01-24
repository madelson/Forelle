using Medallion.Collections;
using System;
using System.Collections.Generic;
using System.Text;
using System.Linq;
using System.Collections.Immutable;

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

        public static IEnumerable<TKey> Keys<TKey, TValue>(this ILookup<TKey, TValue> lookup)
        {
            return (lookup ?? throw new ArgumentNullException(nameof(lookup)))
                .Select(g => g.Key);
        }

        // todo clean up
        //public static ImmutableHashSet<T> RemoveAll<T>(this ImmutableHashSet<T> set, Func<T, bool> predicate)
        //{
        //    if (set == null) { throw new ArgumentNullException(nameof(set)); }
        //    if (predicate == null) { throw new ArgumentNullException(nameof(predicate)); }

        //    ImmutableHashSet<T>.Builder builder = null;
        //    foreach (var item in set)
        //    {
        //        if (predicate(item))
        //        {
        //            (builder ?? (builder = set.ToBuilder())).Remove(item);
        //        }
        //    }

        //    return builder?.ToImmutable() ?? set;
        //}
    }
}
