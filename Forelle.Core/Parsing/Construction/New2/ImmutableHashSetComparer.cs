using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text;

namespace Forelle.Parsing.Construction.New2
{
    internal sealed class ImmutableHashSetComparer<T> : IEqualityComparer<ImmutableHashSet<T>>
    {
        public static readonly ImmutableHashSetComparer<T> Instance = new ImmutableHashSetComparer<T>();

        private ImmutableHashSetComparer() { }

        public bool Equals(ImmutableHashSet<T> x, ImmutableHashSet<T> y)
        {
            if (x == y) { return true; }
            if (x == null || y == null || x.Count != y.Count || !Equals(x.KeyComparer, y.KeyComparer)) { return false; }

            // This is better than ImmutableHashSet.SetEquals() since currently 
            // that method is not optimized for comparing 2 sets with the same comparer
            foreach (var item in x)
            {
                if (!y.Contains(item)) { return false; }
            }
            return true;
        }

        public int GetHashCode(ImmutableHashSet<T> obj)
        {
            if (obj == null) { return 0; }

            var hash = obj.KeyComparer.GetHashCode();
            foreach (var item in obj)
            {
                hash ^= obj.KeyComparer.GetHashCode(item);
            }
            return hash;
        }
    }
}
