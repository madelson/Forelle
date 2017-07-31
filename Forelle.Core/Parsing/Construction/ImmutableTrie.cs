using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text;

namespace Forelle.Parsing.Construction
{
    /// <summary>
    /// Maps a sequence of <typeparamref name="TKey"/>s to a set of <typeparamref name="TValue"/>
    /// </summary>
    internal sealed class ImmutableTrie<TKey, TValue>
    {
        public static ImmutableTrie<TKey, TValue> Empty { get; } = new ImmutableTrie<TKey, TValue>(Node.Empty);

        private readonly Node _root;

        private ImmutableTrie(Node root)
        {
            this._root = root;
        }

        public ImmutableTrie<TKey, TValue> Add(IEnumerable<TKey> keys, TValue value)
        {
            if (keys == null) { throw new ArgumentNullException(nameof(keys)); }

            using (var enumerator = keys.GetEnumerator())
            {
                var newRoot = this._root.Add(enumerator, value);
                return newRoot != this._root ? new ImmutableTrie<TKey, TValue>(newRoot) : this;
            }
        }

        /// <summary>
        /// Gets all values associated with the sequence <paramref name="keys"/>
        /// </summary>
        public ImmutableHashSet<TValue> this[IEnumerable<TKey> keys]
        {
            get => this.GetInternal(keys, includePrefixValues: false);
        }

        /// <summary>
        /// Gets all values associated with the sequence <paramref name="keys"/> or any prefix of that sequence
        /// </summary>
        public ImmutableHashSet<TValue> GetWithPrefixValues(IEnumerable<TKey> keys) => this.GetInternal(keys, includePrefixValues: true);

        private ImmutableHashSet<TValue> GetInternal(IEnumerable<TKey> keys, bool includePrefixValues)
        {
            if (keys == null) { throw new ArgumentNullException(nameof(keys)); }

            using (var enumerator = keys.GetEnumerator())
            {
                return this._root.Get(enumerator, includePrefixValues);
            }
        }

        private sealed class Node
        {
            public static readonly Node Empty = new Node(ImmutableDictionary<TKey, Node>.Empty, ImmutableHashSet<TValue>.Empty);
            
            private Node(ImmutableDictionary<TKey, Node> children, ImmutableHashSet<TValue> values)
            {
                this.Children = children;
                this.Values = values;
            }

            public ImmutableDictionary<TKey, Node> Children { get; }
            public ImmutableHashSet<TValue> Values { get; }

            private Node Update(ImmutableDictionary<TKey, Node> children = null, ImmutableHashSet<TValue> values = null)
            {
                var newChildren = children ?? this.Children;
                var newValues = values ?? this.Values;
                return newChildren != this.Children || newValues != this.Values
                    ? new Node(newChildren, newValues)
                    : this;
            }

            public Node Add(IEnumerator<TKey> keys, TValue value)
            {
                if (keys.MoveNext())
                {
                    var key = keys.Current;
                    var child = this.Children.TryGetValue(key, out var existing) ? existing : Empty;
                    var newChild = child.Add(keys, value);
                    return this.Update(children: this.Children.SetItem(key, newChild));
                }

                return this.Update(values: this.Values.Add(value));
            }

            public ImmutableHashSet<TValue> Get(IEnumerator<TKey> keys, bool includePrefixValues)
            {
                if (keys.MoveNext())
                {
                    var childResult = this.Children.TryGetValue(keys.Current, out var child)
                        ? child.Get(keys, includePrefixValues)
                        : ImmutableHashSet<TValue>.Empty;
                    return includePrefixValues ? childResult.Union(this.Values) : childResult;
                }

                return this.Values;
            }
        }
    }
}
