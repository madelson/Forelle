using Medallion.Collections;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Forelle
{
    /// <summary>
    /// A simple mutable implementation of the <see cref="ILookup{TKey, TElement}"/> interface. Essentially a
    /// multi-dictionary
    /// </summary>
    internal sealed class Lookup<TKey, TValue> : ILookup<TKey, TValue>, IReadOnlyCollection<IGrouping<TKey, TValue>>
    {
        private readonly Dictionary<TKey, Grouping> _dictionary;

        public Lookup(IEqualityComparer<TKey> comparer = null)
        {
            this._dictionary = new Dictionary<TKey, Grouping>(comparer);
        }

        public void Add(TKey key, TValue value)
        {
            (this._dictionary.TryGetValue(key, out var grouping) ? grouping : (this._dictionary[key] = new Grouping(key)))
                .Add(value);
        }

        public IEnumerable<TValue> this[TKey key] => this._dictionary.TryGetValue(key, out var grouping) ? grouping : Empty.ReadOnlyCollection<TValue>();

        public int Count => this._dictionary.Count;

        public bool Contains(TKey key) => this._dictionary.ContainsKey(key);

        public IEnumerator<IGrouping<TKey, TValue>> GetEnumerator() => this._dictionary.Values.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => this.GetEnumerator();

        private sealed class Grouping : List<TValue>, IGrouping<TKey, TValue>
        {
            public Grouping(TKey key)
            {
                this.Key = key;
            }

            public TKey Key { get; }
        }
    }
}
