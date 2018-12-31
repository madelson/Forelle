using Medallion.Collections;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Forelle.Tests
{
    public static class TestHelper
    {
        public static T ShouldEqual<T>(this T actual, T expected, string message = null)
        {
            Assert.AreEqual(actual: actual, expected: expected, message: message);
            return actual;
        }

        public static T ShouldNotEqual<T>(this T actual, T expected, string message = null)
        {
            Assert.AreNotEqual(actual: actual, expected: expected, message: message);
            return actual;
        }

        public static string ShouldEqualIgnoreIndentation(this string actual, string expected, string message = null)
        {
            return StripIndendation(actual).ShouldEqual(StripIndendation(expected));
        }

        public static string StripIndendation(string text)
        {
            return text == null ? null : Regex.Replace(text.Trim(), @"\r?\n[ \t]*", "\r\n");
        }

        public static IEnumerable<T> CollectionShouldEqual<T>(this IEnumerable<T> actual, IEnumerable<T> expected, string message = null)
        {
            if (expected == null)
            {
                return actual.ShouldEqual(null, message);
            }

            actual.ShouldNotEqual(null, message);

            var sortedActual = actual.SortForCollectionShouldEqual().ToArray();
            var sortedExpected = expected.SortForCollectionShouldEqual().ToArray();

            var detailedMessage = (message != null ? message + ": " + Environment.NewLine + Environment.NewLine : string.Empty)
                + $"Missing from actual: {Environment.NewLine}{string.Join(Environment.NewLine, expected.Except(actual).Select(t => "\t" + t))}"
                + Environment.NewLine + Environment.NewLine
                + $"Unexpected in actual: {Environment.NewLine}{string.Join(Environment.NewLine, actual.Except(expected).Select(t => "\t" + t))}";

            CollectionAssert.AreEquivalent(expected?.SortForCollectionShouldEqual(), actual?.SortForCollectionShouldEqual(), detailedMessage);
            return actual;
        }

        /// <summary>
        /// Best-effort sorts the sequence to improve the error message from <see cref="CollectionAssert.AreEquivalent(System.Collections.IEnumerable, System.Collections.IEnumerable, string, object[])"/>
        /// </summary>
        private static IEnumerable<T> SortForCollectionShouldEqual<T>(this IEnumerable<T> @this)
        {
            var isComparable = (typeof(IComparable<T>).GetTypeInfo().IsAssignableFrom(typeof(T))
                || typeof(IComparable).GetTypeInfo().IsAssignableFrom(typeof(T)))
                // tuple types are IComparable but comparison fails if the items are not comparable
                && !typeof(T).ToString().StartsWith("System.ValueTuple")
                && !typeof(T).ToString().StartsWith("System.Tuple");

            var comparer = isComparable ? Comparer<T>.Default : Comparers.Create((T item) => EqualityComparer<T>.Default.GetHashCode(item));
            return @this.OrderBy(t => t, comparer);
        }
    }
}
