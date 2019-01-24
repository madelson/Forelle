using System;
using System.Collections.Generic;
using System.Text;

namespace Forelle.Parsing.Construction.New2
{
    internal sealed class PotentialParseNodeWithCursorComparer : IEqualityComparer<PotentialParseNode>
    {
        public static readonly IEqualityComparer<PotentialParseNode> Instance = new PotentialParseNodeWithCursorComparer();

        private PotentialParseNodeWithCursorComparer() { }

        public bool Equals(PotentialParseNode x, PotentialParseNode y)
        {
            if (x == y) { return true; }
            if (x == null 
                || y == null 
                || x.Symbol != y.Symbol
                || x.LeafCount != y.LeafCount
                || x.CursorPosition != y.CursorPosition)
            {
                return false;
            }

            return x is PotentialParseParentNode xAsParent
                ? y is PotentialParseParentNode yAsParent && this.Equals(xAsParent, yAsParent)
                : y is PotentialParseLeafNode;
        }

        private bool Equals(PotentialParseParentNode x, PotentialParseParentNode y)
        {
            if (x.Rule != y.Rule) { return false; }

            for (var i = 0; i < x.Children.Count; ++i)
            {
                if (!this.Equals(x.Children[i], y.Children[i])) { return false; }
            }

            return true;
        }

        public int GetHashCode(PotentialParseNode obj)
        {
            switch (obj)
            {
                case null: return 0;
                case PotentialParseParentNode parent:
                    var hash = parent.Rule.GetHashCode();
                    for (var i = 0; i < parent.Children.Count; ++i)
                    {
                        hash = (hash, this.GetHashCode(parent.Children[i])).GetHashCode();
                    }
                    return hash;
                default:
                    return (obj.Symbol, obj.CursorPosition).GetHashCode();
            }
        }
    }
}
