using Medallion.Collections;
using System;
using System.Collections.Generic;
using System.Text;

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

        public static IEqualityComparer<(T1, T2)> CreateTupleComparer<T1, T2>(
            IEqualityComparer<T1> comparer1 = null,
            IEqualityComparer<T2> comparer2 = null)
        {
            var comparer1ToUse = comparer1 ?? EqualityComparer<T1>.Default;
            var comparer2ToUse = comparer2 ?? EqualityComparer<T2>.Default;
            return EqualityComparers.Create<(T1, T2)>(
                equals: (a, b) => comparer1ToUse.Equals(a.Item1, b.Item1)
                    && comparer2ToUse.Equals(a.Item2, b.Item2),
                hash: t => (comparer1ToUse.GetHashCode(t.Item1), comparer2ToUse.GetHashCode(t.Item2)).GetHashCode()
            );
        }
    }
}
