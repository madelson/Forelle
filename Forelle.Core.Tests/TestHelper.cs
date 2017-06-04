using Medallion.Collections;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace Forelle.Core.Tests
{
    public static class TestHelper
    {
        public static T ShouldEqual<T>(this T actual, T expected, string message = null)
        {
            Assert.AreEqual(actual: actual, expected: expected, message: message);
            return actual;
        }

        public static IEnumerable<T> CollectionShouldEqual<T>(this IEnumerable<T> actual, IEnumerable<T> expected, string message = null)
        {
            CollectionAssert.AreEquivalent(expected?.SortForCollectionShouldEqual(), actual?.SortForCollectionShouldEqual(), message);
            return actual;
        }

        /// <summary>
        /// Best-effort sorts the sequence to improve the error message from <see cref="CollectionAssert.AreEquivalent(System.Collections.IEnumerable, System.Collections.IEnumerable, string, object[])"/>
        /// </summary>
        private static IEnumerable<T> SortForCollectionShouldEqual<T>(this IEnumerable<T> @this)
        {
            var isComparable = typeof(IComparable<T>).GetTypeInfo().IsAssignableFrom(typeof(T))
                || typeof(IComparable).GetTypeInfo().IsAssignableFrom(typeof(T));

            var comparer = isComparable ? Comparer<T>.Default : Comparers.Create((T item) => EqualityComparer<T>.Default.GetHashCode(item));
            return @this.OrderBy(t => t, comparer);
        }
    }
}
