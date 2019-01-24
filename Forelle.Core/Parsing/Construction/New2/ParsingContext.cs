using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text;

namespace Forelle.Parsing.Construction.New2
{
    internal sealed class ParsingContext
    {
        private int _cachedHashCode;

        public ParsingContext(PotentialParseParentNode node, IEnumerable<Token> lookaheadTokens)
        {
            this.Nodes = ImmutableHashSet.Create<PotentialParseParentNode>(
                PotentialParseNodeWithCursorComparer.Instance, 
                node ?? throw new ArgumentNullException(nameof(node))
            );
            this.LookaheadTokens = lookaheadTokens.ToImmutableHashSet();
            VerifyNonEmptyAndAllNonNull(this.LookaheadTokens, nameof(lookaheadTokens));
        }

        public ParsingContext(IEnumerable<PotentialParseParentNode> nodes, IEnumerable<Token> lookaheadTokens)
        {
            this.Nodes = nodes.ToImmutableHashSet<PotentialParseParentNode>(PotentialParseNodeWithCursorComparer.Instance);
            VerifyNonEmptyAndAllNonNull(this.Nodes, nameof(nodes));
            this.LookaheadTokens = lookaheadTokens.ToImmutableHashSet();
            VerifyNonEmptyAndAllNonNull(this.LookaheadTokens, nameof(lookaheadTokens));
        }

        public ImmutableHashSet<PotentialParseParentNode> Nodes { get; }
        public ImmutableHashSet<Token> LookaheadTokens { get; }

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

        private static void VerifyNonEmptyAndAllNonNull<T>(ImmutableHashSet<T> set, string parameterName) where T : class
        {
            if (set.IsEmpty) { throw new ArgumentException("may not be empty", parameterName); }
            if (set.Contains(null)) { throw new ArgumentException("may not contain null", parameterName); }
        }
    }
}
