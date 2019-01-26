using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;

namespace Forelle.Parsing.Construction.New2
{
    internal sealed class ParsingContext
    {
        private int _cachedHashCode;
        
        public ParsingContext(IEnumerable<PotentialParseParentNode> nodes, IEnumerable<Token> lookaheadTokens)
        {
            this.Nodes = nodes.ToImmutableHashSet<PotentialParseParentNode>(PotentialParseNodeWithCursorComparer.Instance);
            if (this.Nodes.IsEmpty) { throw new ArgumentException("may not be empty", nameof(nodes)); }
            if (this.Nodes.Contains(null)) { throw new ArgumentException("may not contain null", nameof(nodes)); }

            this.LookaheadTokens = lookaheadTokens.ToImmutableHashSet();
            if (this.LookaheadTokens.Contains(null)) { throw new ArgumentException("may not contain null", nameof(nodes)); }
        }

        public ImmutableHashSet<PotentialParseParentNode> Nodes { get; }
        public ImmutableHashSet<Token> LookaheadTokens { get; }

        private string DebugView => this.ToString();

        public override bool Equals(object obj) => obj is ParsingContext that
            && (this._cachedHashCode == default || that._cachedHashCode == default || this._cachedHashCode == that._cachedHashCode)
            && ImmutableHashSetComparer<PotentialParseParentNode>.Instance.Equals(this.Nodes, that.Nodes)
            && ImmutableHashSetComparer<Token>.Instance.Equals(this.LookaheadTokens, that.LookaheadTokens);

        public override int GetHashCode()
        {
            if (this._cachedHashCode == default)
            {
                this._cachedHashCode = (
                        ImmutableHashSetComparer<PotentialParseParentNode>.Instance.GetHashCode(this.Nodes),
                        ImmutableHashSetComparer<Token>.Instance.GetHashCode(this.LookaheadTokens)
                    )
                    .GetHashCode();
            }

            return this._cachedHashCode;
        }

        public override string ToString() =>
            string.Join(
                Environment.NewLine,
                this.Nodes.Select(n => n.ToMarkedString())
                    .OrderBy(s => s, StringComparer.Ordinal)
            )
            + Environment.NewLine
            + $"LOOKAHEAD [{string.Join(", ", this.LookaheadTokens.OrderBy(s => s.Name, StringComparer.Ordinal))}]";
    }
}
