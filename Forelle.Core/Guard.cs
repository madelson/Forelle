using System;
using System.Collections.Generic;
using System.Text;
using System.Linq;

namespace Forelle
{
    internal static class Guard
    {
        public static void NotNullOrContainsNull<T>(IReadOnlyCollection<T> items, string paramName)
        {
            if (items?.Contains((T)default(object)) ?? throw new ArgumentNullException(paramName))
            {
                throw new ArgumentException("must not contain null", paramName);
            }
        }

        public static T[] NotNullOrContainsNull<T>(IEnumerable<T> items, string paramName)
        {
            var array = items?.ToArray();
            NotNullOrContainsNull(array, paramName);
            return array;
        }
    }
}
