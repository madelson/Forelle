using System;
using System.Collections.Generic;
using System.Text;

namespace Forelle.Parsing.Preprocessing.LR
{
    /// <summary>
    /// Wraps a <see cref="PotentialParseParentNode"/> to provide structure and cursor-based equality semantics
    /// </summary>
    internal struct LRRule : IEquatable<LRRule>
    {
        private static readonly IEqualityComparer<PotentialParseParentNode> PotentialParseParentNodeComparer = Construction.New2.PotentialParseNodeWithCursorComparer.Instance;

        public LRRule(PotentialParseParentNode node)
        {
            Invariant.Require(node.CursorPosition.HasValue);

            this.Node = node;
        }

        public PotentialParseParentNode Node { get; }

        public bool Equals(LRRule other) => PotentialParseParentNodeComparer.Equals(this.Node, other.Node);

        public override bool Equals(object obj) => obj is LRRule that && this.Equals(that);

        public override int GetHashCode() => PotentialParseParentNodeComparer.GetHashCode(this.Node);

        public override string ToString()
        {
            var builder = new StringBuilder();
            builder.Append(this.Node.Symbol).Append(" -> ");
            if (this.Node.Children.Count == 0)
            {
                builder.Append('.');
            }
            else 
            {
                AppendChildren(this.Node);
            } 

            return builder.ToString();

            void Append(PotentialParseNode node)
            {
                if (node is PotentialParseParentNode parent)
                {
                    builder.Append(parent.Symbol)
                        .Append('(');
                    AppendChildren(parent);
                    builder.Append(')');
                }
                else
                {
                    if (node.CursorPosition == 0)
                    {
                        builder.Append('.');
                    }
                    builder.Append(node.Symbol);
                    if (node.HasTrailingCursor())
                    {
                        builder.Append('.');
                    }
                }
            }

            void AppendChildren(PotentialParseParentNode parent)
            {
                for (var i = 0; i < parent.Children.Count; ++i)
                {
                    if (i != 0) { builder.Append(' '); }
                    Append(parent.Children[i]);
                }
            }
        }
    }
}
