using Medallion.Collections;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Forelle.Parsing.Preprocessing.LR
{
    internal sealed class LRClosure : IEquatable<LRClosure>, IReadOnlyDictionary<RuleRemainder, LRLookahead>
    {
        private static readonly IEqualityComparer<IReadOnlyDictionary<RuleRemainder, LRLookahead>> DictionaryComparer =
            EqualityComparers.GetCollectionComparer<KeyValuePair<RuleRemainder, LRLookahead>>();
        private const int DefaultCachedHashCode = -17;

        private readonly Dictionary<RuleRemainder, LRLookahead> _items;
        private int _cachedHashCode = DefaultCachedHashCode;

        public LRClosure(Dictionary<RuleRemainder, LRLookahead> items)
        {
            Invariant.Require(items.Count != 0);
            this._items = items;
        }

        public IEnumerable<RuleRemainder> Keys => this._items.Keys;

        public IEnumerable<LRLookahead> Values => this._items.Values;

        public int Count => this._items.Count;

        public LRLookahead this[RuleRemainder key] => this._items[key];

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

        public bool ContainsKey(RuleRemainder key) => this._items.ContainsKey(key);

        public bool TryGetValue(RuleRemainder key, out LRLookahead value) => this._items.TryGetValue(key, out value);

        public Dictionary<RuleRemainder, LRLookahead>.Enumerator GetEnumerator() => this._items.GetEnumerator();

        IEnumerator<KeyValuePair<RuleRemainder, LRLookahead>> IEnumerable<KeyValuePair<RuleRemainder, LRLookahead>>.GetEnumerator() => this.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => this.GetEnumerator();
    }
}
