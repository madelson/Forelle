using Medallion.Collections;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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

        private IReadOnlyList<PotentialParseLeafNode> _cachedLeaves;
        internal IReadOnlyList<PotentialParseLeafNode> Leaves => this._cachedLeaves ?? (this._cachedLeaves = this.GetLeaves());

        private protected abstract IReadOnlyList<PotentialParseLeafNode> GetLeaves();
        private protected abstract int GetValueHashCode();

        private int _cachedValueHashCode;

        public static PotentialParseNode Create(Symbol symbol) => new PotentialParseLeafNode(symbol);
        public static PotentialParseNode Create(Rule rule) => Create(rule, rule?.Symbols.Select(Create));
        public static PotentialParseNode Create(Rule rule, IEnumerable<PotentialParseNode> children) => new PotentialParseParentNode(rule, children);
        public static PotentialParseNode Create(Rule rule, params SymbolRuleOrNode[] children) => Create(rule, children?.Select(n => n.Node));

        private protected static string ToString(Symbol symbol) => symbol.Name.Any(char.IsWhiteSpace)
            || symbol.Name.IndexOf('(') >= 0
            || symbol.Name.IndexOf(')') >= 0
            ? $"\"{symbol.Name}\""
            : symbol.Name;

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
        public PotentialParseLeafNode(Symbol symbol)
            : base(symbol)
        {
        }
        
        public override string ToString() => ToString(this.Symbol);
        
        private protected override IReadOnlyList<PotentialParseLeafNode> GetLeaves() => new[] { this };
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
                if (this.Children[i].Symbol != this.Rule.Symbols[i])
                {
                    throw new ArgumentException($"Incorrect symbol type for {nameof(children)}[{i}]. Expected '{this.Rule.Symbols[i]}', but found '{this.Children[i].Symbol}'.", nameof(children));
                }
            }
        }

        public Rule Rule { get; }
        public new NonTerminal Symbol => this.Rule.Produced;
        public IReadOnlyList<PotentialParseNode> Children { get; }

        public override string ToString() => $"{ToString(this.Symbol)}({string.Join(" ", this.Children)})";

        private protected override IReadOnlyList<PotentialParseLeafNode> GetLeaves()
        {
            var leafCount = 0;
            for (var i = 0; i < this.Children.Count; ++i)
            {
                leafCount += this.Children[i].Leaves.Count;
            }

            var leaves = new PotentialParseLeafNode[leafCount];
            var leavesIndex = 0;
            for (var i = 0; i < this.Children.Count; ++i)
            {
                var childLeaves = this.Children[i].Leaves;
                for (var j = 0; j < childLeaves.Count; ++j)
                {
                    leaves[leavesIndex++] = childLeaves[j];
                }
            }

            return leaves;
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
