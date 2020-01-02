using Medallion.Collections;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;

namespace Forelle.Parsing.Preprocessing.LR
{
    internal readonly struct LRLookahead : IEquatable<LRLookahead>
    {
        public LRLookahead(ImmutableHashSet<Token> tokens)
        {
            Invariant.Require(!tokens.Contains(null));
            this.Tokens = tokens;
        }

        public ImmutableHashSet<Token> Tokens { get; }

        public override bool Equals(object obj) => obj is LRLookahead that && this.Equals(that);

        public bool Equals(LRLookahead other)
        {
            if (other.Tokens == this.Tokens) { return true; }
            if (other.Tokens.Count != this.Tokens.Count) { return false; }

            foreach (var token in other.Tokens)
            {
                if (!this.Tokens.Contains(token)) { return false; }
            }

            return true;
        }

        public override int GetHashCode()
        {
            var hash = 17;
            foreach (var token in this.Tokens)
            {
                hash = (hash, token).GetHashCode();
            }
            return hash;
        }

        public override string ToString() => this.Tokens.Count == 1 ? this.Tokens.First().ToString() : $"{{{string.Join(", ", this.Tokens.OrderBy(t => t.Name))}}}";
    }
}
