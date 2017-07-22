using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Forelle.Parsing
{
    /// <summary>
    /// Wrapper for a <see cref="ParserNode"/> that flattens out <see cref="ReferenceNode"/>s
    /// </summary>
    internal sealed class NodeContainer
    {
        private readonly ParserNode _node;

        public NodeContainer(ParserNode node)
        {
            this._node = node ?? throw new ArgumentNullException(nameof(node));
        }

        public ParserNode Value => this._node is ReferenceNode reference && reference.TryGetValue(out var value) ? value : this._node;
    }

    /// <summary>
    /// Holds either a <see cref="Token"/> or a <see cref="ParserNode"/>. Flattens out
    /// <see cref="ReferenceNode"/>s upon retrieval
    /// </summary>
    internal class TokenOrParserNode
    {
        private readonly NodeContainer _nodeContainer;
        
        public TokenOrParserNode(ParserNode node)
        {
            this._nodeContainer = new NodeContainer(node);
        }

        public TokenOrParserNode(Token token)
        {
            this.Token = token ?? throw new ArgumentNullException(nameof(token));
        }

        public ParserNode Node => this._nodeContainer?.Value;
        public Token Token { get; }
    }

    /// <summary>
    /// A list of <see cref="ParserNode"/>s backed by <see cref="NodeContainer"/>s
    /// to flatten <see cref="ReferenceNode"/>s
    /// </summary>
    internal sealed class NodeList : IReadOnlyList<ParserNode>
    {
        private readonly IReadOnlyList<NodeContainer> _nodes;

        public NodeList(IEnumerable<ParserNode> nodes)
        {
            if (nodes == null) { throw new ArgumentNullException(nameof(nodes)); }

            this._nodes = nodes.Select(n => new NodeContainer(n)).ToArray();
        }

        public ParserNode this[int index] => this._nodes[index].Value;

        public int Count => this._nodes.Count;

        public IEnumerator<ParserNode> GetEnumerator() => this._nodes.Select(n => n.Value).GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => this.GetEnumerator();
    }

    /// <summary>
    /// A dictionary of <typeparamref name="TKey"/> to <see cref="ParserNode"/> backed
    /// by <see cref="NodeContainer"/>s to flatten <see cref="ReferenceNode"/>s
    /// </summary>
    internal sealed class NodeValueDictionary<TKey> : IReadOnlyDictionary<TKey, ParserNode>
    {
        private readonly IReadOnlyDictionary<TKey, NodeContainer> _dictionary;

        public NodeValueDictionary(IEnumerable<KeyValuePair<TKey, ParserNode>> elements)
        {
            if (elements == null) { throw new ArgumentNullException(nameof(elements)); }

            this._dictionary = elements.ToDictionary(kvp => kvp.Key, kvp => new NodeContainer(kvp.Value));
        }

        public ParserNode this[TKey key] => this._dictionary[key].Value;

        public IEnumerable<TKey> Keys => this._dictionary.Keys;

        public IEnumerable<ParserNode> Values => this._dictionary.Values.Select(c => c.Value);

        public int Count => this._dictionary.Count;

        public bool ContainsKey(TKey key) => this._dictionary.ContainsKey(key);

        public IEnumerator<KeyValuePair<TKey, ParserNode>> GetEnumerator()
        {
            return this._dictionary.Select(kvp => new KeyValuePair<TKey, ParserNode>(kvp.Key, kvp.Value.Value)).GetEnumerator();
        }

        public bool TryGetValue(TKey key, out ParserNode value)
        {
            if (this._dictionary.TryGetValue(key, out var containerValue))
            {
                value = containerValue.Value;
                return true;
            }

            value = null;
            return false;
        }

        IEnumerator IEnumerable.GetEnumerator() => this.GetEnumerator();
    }
}
