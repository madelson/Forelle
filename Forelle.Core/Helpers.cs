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
    }
}
