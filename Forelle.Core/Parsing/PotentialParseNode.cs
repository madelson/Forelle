using Medallion.Collections;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Text;

namespace Forelle.Parsing
{
    public abstract class PotentialParseNode
    {
        public static readonly IEqualityComparer<PotentialParseNode> Comparer = new NodeComparer();

        internal PotentialParseNode(Symbol symbol)
        {
            this.Symbol = symbol ?? throw new ArgumentNullException(nameof(symbol));
        }

        public Symbol Symbol { get; }
        internal abstract int? CursorPosition { get; } 
        internal abstract int LeafCount { get; }
        
        private protected abstract int GetValueHashCode();

        private int _cachedValueHashCode;

        public sealed override string ToString()
        {
            var builder = new StringBuilder();
            this.ToString(builder, renderCursorOnly: false);
            return builder.ToString();
        }

        internal string ToMarkedString()
        {
            var builder = new StringBuilder();
            this.ToString(builder, renderCursorOnly: false);
            if (this.CursorPosition.HasValue)
            {
                builder.AppendLine();
                this.ToString(builder, renderCursorOnly: true);
                if (this.HasTrailingCursor())
                {
                    builder.Append(CursorMark);
                }
            }

            return builder.ToString();
        }

        internal abstract void ToString(StringBuilder builder, bool renderCursorOnly);

        // todo could cache default creates (at least symbol) with the Symbol class
        public static PotentialParseNode Create(Symbol symbol) => new PotentialParseLeafNode(symbol);
        public static PotentialParseNode Create(Rule rule) => Create(rule, rule?.Symbols.Select(Create));
        public static PotentialParseNode Create(Rule rule, IEnumerable<PotentialParseNode> children) => new PotentialParseParentNode(rule, children);
        public static PotentialParseNode Create(Rule rule, params SymbolRuleOrNode[] children) => Create(rule, children?.Select(n => n.Node));

        private protected const char CursorMark = '^', CursorSpacer = '.';
        private protected static string ToString(Symbol symbol) => symbol.Name.Any(char.IsWhiteSpace)
            || symbol.Name.IndexOf('(') >= 0
            || symbol.Name.IndexOf(')') >= 0
            ? $"\"{symbol.Name}\""
            : symbol.Name;

        private string DebugView => this.ToMarkedString();

        public struct SymbolRuleOrNode
        {
            internal SymbolRuleOrNode(PotentialParseNode node)
            {
                this.Node = node;
            }

            internal PotentialParseNode Node { get; }

            public static implicit operator SymbolRuleOrNode(Symbol symbol) => symbol != null ? Create(symbol) : null;
            public static implicit operator SymbolRuleOrNode(Rule rule) => rule != null ? Create(rule) : null;
            public static implicit operator SymbolRuleOrNode(PotentialParseNode node) => new SymbolRuleOrNode(node); 
        }

        private sealed class NodeComparer : IEqualityComparer<PotentialParseNode>
        {
            public bool Equals(PotentialParseNode x, PotentialParseNode y)
            {
                if (x == y) { return true; }
                if (x == null || y == null) { return false; }
                if (x.Symbol != y.Symbol) { return false; }
                if (TryGetCachedHashCode(x, out var xHash)
                    && TryGetCachedHashCode(y, out var yHash)
                    && xHash != yHash)
                {
                    return false;
                }

                if (x is PotentialParseParentNode parentX)
                {
                    if (y is PotentialParseParentNode parentY)
                    {
                        if (parentX.Children.Count != parentY.Children.Count) { return false; }
                        for (var i = 0; i < parentX.Children.Count; ++i)
                        {
                            if (!this.Equals(parentX.Children[i], parentY.Children[i])) { return false; }
                        }
                    }
                    else
                    {
                        return false;
                    }
                }

                return true;
            }

            public int GetHashCode(PotentialParseNode obj)
            {
                if (obj == null) { return 0; }
                if (TryGetCachedHashCode(obj, out var cachedHash)) { return cachedHash; }

                return obj._cachedValueHashCode = obj.GetValueHashCode();
            }

            private static bool TryGetCachedHashCode(PotentialParseNode node, out int hash)
            {
                hash = node._cachedValueHashCode;
                return hash != default;
            }
        }
    }

    internal sealed class PotentialParseLeafNode : PotentialParseNode
    {
        public PotentialParseLeafNode(Symbol symbol, int? cursorPosition = null)
            : base(symbol)
        {
            Invariant.Require(!cursorPosition.HasValue || cursorPosition == 0 || cursorPosition == 1);
            this.CursorPosition = cursorPosition;
        }

        internal override int? CursorPosition { get; }
        internal override int LeafCount => 1;
        
        internal override void ToString(StringBuilder builder, bool renderCursorOnly)
        {
            var symbolString = ToString(this.Symbol);
            if (renderCursorOnly)
            {
                if (this.CursorPosition == 0)
                {
                    builder.Append(CursorMark)
                        .Append(CursorSpacer, repeatCount: symbolString.Length - 1);
                }
                else
                {
                    builder.Append(CursorSpacer, repeatCount: symbolString.Length);
                }
            }
            else
            {
                builder.Append(symbolString);
            }
        }
        
        private protected override int GetValueHashCode() => this.Symbol.GetHashCode();
    }

    internal sealed class PotentialParseParentNode : PotentialParseNode
    {
        public PotentialParseParentNode(Rule rule, IEnumerable<PotentialParseNode> children)
            : base((rule ?? throw new ArgumentNullException(nameof(rule))).Produced)
        {
            this.Rule = rule;
            this.Children = Guard.NotNullOrContainsNullAndDefensiveCopy(children, nameof(children));
            if (this.Children.Count != this.Rule.Symbols.Count)
            {
                throw new ArgumentException($"Child count must match {nameof(rule)} symbol count", nameof(children));
            }

            for (var i = 0; i < this.Children.Count; ++i)
            {
                var child = this.Children[i];
                if (child.Symbol != this.Rule.Symbols[i])
                {
                    throw new ArgumentException($"Incorrect symbol type for {nameof(children)}[{i}]. Expected '{this.Rule.Symbols[i]}', but found '{child.Symbol}'.", nameof(children));
                }

                this.LeafCount += child.LeafCount;

                if (child.CursorPosition.HasValue)
                {
                    Invariant.Require(!this.CursorPosition.HasValue, "at most one child may have a cursor set");
                    if (child.HasTrailingCursor())
                    {
                        Invariant.Require(i == this.Children.Count - 1, "only a trailing child may have a trailing cursor");
                        this.CursorPosition = i + 1;
                    }
                    else
                    {
                        this.CursorPosition = i;
                    }
                }
            }
        }

        public PotentialParseParentNode(Rule rule, bool hasTrailingCursor)
            : this(rule, Enumerable.Empty<PotentialParseNode>())
        {
            if (hasTrailingCursor)
            {
                this.CursorPosition = 0;
            }
        }

        public Rule Rule { get; }
        public new NonTerminal Symbol => this.Rule.Produced;
        public IReadOnlyList<PotentialParseNode> Children { get; }
        internal override int? CursorPosition { get; }
        internal override int LeafCount { get; }

        internal override void ToString(StringBuilder builder, bool renderCursorOnly)
        {
            var producedSymbolString = ToString(this.Symbol);
            if (renderCursorOnly)
            {
                builder.Append(CursorSpacer, repeatCount: producedSymbolString.Length + 1);
            }
            else
            {
                builder.Append(producedSymbolString).Append('(');
            }

            for (var i = 0; i < this.Children.Count; ++i)
            {
                if (i > 0) { builder.Append(renderCursorOnly ? CursorSpacer : ' '); }
                this.Children[i].ToString(builder, renderCursorOnly);
            }

            builder.Append(renderCursorOnly ? CursorSpacer : ')');
        }

        private protected override int GetValueHashCode()
        {
            var hash = this.Rule.GetHashCode();
            for (var i = 0; i < this.Children.Count; ++i)
            {
                hash = (hash, Comparer.GetHashCode(this.Children[i])).GetHashCode();
            }
            return hash;
        }
    }
}
