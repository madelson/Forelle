using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
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
            CollectionAssert.AreEquivalent(expected, actual, message);
            return actual;
        }
    }
}
