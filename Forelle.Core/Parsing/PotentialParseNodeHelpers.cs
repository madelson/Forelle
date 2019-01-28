using Medallion.Collections;
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
            if (node == null) { throw new ArgumentNullException(nameof(node)); }

            return node is PotentialParseParentNode parent
                ? node.CursorPosition == parent.Children.Count
                : node.CursorPosition == 1;
        }

        public static TNode WithoutCursor<TNode>(this TNode node)
            where TNode : PotentialParseNode
        {
            var result = !node.CursorPosition.HasValue ? node
                : node is PotentialParseParentNode parent ? new PotentialParseParentNode(parent.Rule, parent.Children.Select(WithoutCursor))
                : new PotentialParseLeafNode(node.Symbol).As<PotentialParseNode>();
            return (TNode)result;
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

        public static TNode WithTrailingCursor<TNode>(this TNode node)
            where TNode : PotentialParseNode
        {
            if (node.HasTrailingCursor()) { return node; }

            PotentialParseNode result;
            if (node is PotentialParseParentNode parent)
            {
                result = parent.Children.Count == 0
                    ? new PotentialParseParentNode(parent.Rule, hasTrailingCursor: true)
                    : new PotentialParseParentNode(
                        parent.Rule,
                        parent.Children.Select((ch, i) => i == parent.Children.Count - 1 ? ch.WithTrailingCursor() : ch)
                    );
            }
            else
            {
                result = new PotentialParseLeafNode(node.Symbol, cursorPosition: 1);
            }

            return (TNode)result;
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

        public static IEnumerable<PotentialParseLeafNode> GetLeaves(this PotentialParseNode node)
        {
            if (node == null) { throw new ArgumentNullException(nameof(node)); }

            return Traverse.DepthFirst(node, n => n is PotentialParseParentNode parent ? parent.Children : Enumerable.Empty<PotentialParseNode>())
                .OfType<PotentialParseLeafNode>();
        }

        public static int CountNodes(this PotentialParseNode node)
        {
            if (node == null) { throw new ArgumentNullException(nameof(node)); }

            return (node is PotentialParseParentNode parent ? parent.Children.Sum(CountNodes) : 0) + 1;
        }
    }
}
