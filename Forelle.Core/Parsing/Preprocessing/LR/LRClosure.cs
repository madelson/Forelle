using Medallion.Collections;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Forelle.Parsing.Preprocessing.LR
{
    internal sealed class LRClosure : IEquatable<LRClosure>, IReadOnlyDictionary<LRRule, LRLookahead>
    {
        private static readonly IEqualityComparer<IReadOnlyDictionary<LRRule, LRLookahead>> DictionaryComparer =
            EqualityComparers.GetCollectionComparer(
                // since KVP doesn't implement GetHashCode() in a useful way
                EqualityComparers.Create((KeyValuePair<LRRule, LRLookahead> kvp) => (kvp.Key, kvp.Value))
            );

        private const int DefaultCachedHashCode = -17;

        private readonly Dictionary<LRRule, LRLookahead> _items;
        private int _cachedHashCode = DefaultCachedHashCode;

        public LRClosure(Dictionary<LRRule, LRLookahead> items)
        {
            Invariant.Require(items.Count != 0);
            this._items = items;
        }

        private string DebugView => this.ToString();

        public IEnumerable<LRRule> Keys => this._items.Keys;

        public IEnumerable<LRLookahead> Values => this._items.Values;

        public int Count => this._items.Count;

        public LRLookahead this[LRRule key] => this._items[key];

        public bool Equals(LRClosure other) => DictionaryComparer.Equals(this._items, other._items);

        public override bool Equals(object obj) => obj is LRClosure that && this.Equals(that);

        public override int GetHashCode()
        {
            if (this._cachedHashCode == DefaultCachedHashCode)
            {
                this._cachedHashCode = DictionaryComparer.GetHashCode(this._items);
            }
            return this._cachedHashCode;
        }

        public override string ToString() => string.Join(
            Environment.NewLine,
            this._items.Select(kvp => $"{kvp.Key} then {kvp.Value}")
                .OrderBy(s => s)
        );

        public bool ContainsKey(LRRule key) => this._items.ContainsKey(key);

        public bool TryGetValue(LRRule key, out LRLookahead value) => this._items.TryGetValue(key, out value);

        public Dictionary<LRRule, LRLookahead>.Enumerator GetEnumerator() => this._items.GetEnumerator();

        IEnumerator<KeyValuePair<LRRule, LRLookahead>> IEnumerable<KeyValuePair<LRRule, LRLookahead>>.GetEnumerator() => this.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => this.GetEnumerator();
    }
}
