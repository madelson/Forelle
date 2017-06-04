using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text;

namespace Forelle
{
    internal static class ImmutableHashSetHelper
    {
        // workarounds for https://github.com/dotnet/corefx/issues/16948
        // this has been fixed (by me), but the new package version isn't out yet

        private static class SingleNullSet<T> where T : class
        {
            public static readonly ImmutableHashSet<T> Instance = ImmutableHashSet.CreateRange(new[] { default(T) });
        }

        public static ImmutableHashSet<T> GetSingleNullSet<T>() where T : class => SingleNullSet<T>.Instance;

        public static ImmutableHashSet<T> AddNull<T>(this ImmutableHashSet<T> set) where T : class
        {
            return set.Union(SingleNullSet<T>.Instance);
        }

        public static ImmutableHashSet<T> RemoveNull<T>(this ImmutableHashSet<T> set) where T : class
        {
            return set.Except(SingleNullSet<T>.Instance);
        }

        public static bool ContainsNull<T>(this ImmutableHashSet<T> set) where T : class
        {
            return set.Except(SingleNullSet<T>.Instance).Count < set.Count;
        }
    }
}
