using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Forelle.Parsing
{
    internal static class PotentialParseNodeHelpers
    {
        public static bool HasTrailingCursor(this PotentialParseNode node)
        {
            return node is PotentialParseParentNode parent
                ? node.CursorPosition == parent.Children.Count
                : node.CursorPosition == 1;
        }

        public static PotentialParseNode WithoutCursor(this PotentialParseNode node)
        {
            return !node.CursorPosition.HasValue ? node
                : node is PotentialParseParentNode parent ? new PotentialParseParentNode(parent.Rule, parent.Children.Select(WithoutCursor))
                : new PotentialParseLeafNode(node.Symbol).As<PotentialParseNode>();
        }

        public static TNode WithCursor<TNode>(this TNode node, int cursorPosition)
            where TNode : PotentialParseNode
        {
            if (node.CursorPosition == cursorPosition) { return node; }
            Invariant.Require(!node.CursorPosition.HasValue);

            if (node is PotentialParseParentNode parent)
            {
                if (parent.Children.Count == 0)
                {
                    Invariant.Require(cursorPosition == 0);
                    return (TNode)new PotentialParseParentNode(parent.Rule, hasTrailingCursor: true).As<PotentialParseNode>();
                }

                Invariant.Require(cursorPosition >= 0 && cursorPosition <= parent.Children.Count);
                PotentialParseNode result = new PotentialParseParentNode(
                    parent.Rule,
                    parent.Children.Select(
                        (ch, i) => i == cursorPosition ? ch.WithCursor(0)
                            : cursorPosition == parent.Children.Count && i == parent.Children.Count - 1 ? ch.WithTrailingCursor()
                            : ch
                    )
                );
                // we can fail to drop the cursor at the intended position if there is an empty (null)
                // child at that position which would push the cursor to the next position
                Invariant.Require(result.CursorPosition == cursorPosition);
                return (TNode)result;
            }

            return (TNode)new PotentialParseLeafNode(node.Symbol, cursorPosition).As<PotentialParseNode>();
        }

        public static PotentialParseNode WithTrailingCursor(this PotentialParseNode node)
        {
            if (node.HasTrailingCursor()) { return node; }

            if (node is PotentialParseParentNode parent)
            {
                if (parent.Children.Count == 0) { return new PotentialParseParentNode(parent.Rule, hasTrailingCursor: true); }

                return new PotentialParseParentNode(
                    parent.Rule,
                    parent.Children.Select((ch, i) => i == parent.Children.Count - 1 ? ch.WithTrailingCursor() : ch)
                );
            }

            return new PotentialParseLeafNode(node.Symbol, cursorPosition: 1);
        }

        public static PotentialParseLeafNode GetLeafAtCursorPosition(this PotentialParseNode node)
        {
            switch (node)
            {
                case PotentialParseParentNode parent: return parent.Children[node.CursorPosition.Value].GetLeafAtCursorPosition();
                case PotentialParseLeafNode leaf: return node.CursorPosition == 0 ? leaf : throw new InvalidOperationException("trailing cursor");
                default: throw new InvalidOperationException("Unexpected node type");
            }
        }
    }
}
