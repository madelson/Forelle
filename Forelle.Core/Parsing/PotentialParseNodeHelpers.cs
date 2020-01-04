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

        public static int GetCursorLeafIndex(this PotentialParseNode node)
        {
            if (node.HasTrailingCursor()) { return node.LeafCount; }

            var result = 0;
            var current = node;
            while (true)
            {
                var cursorPosition = current.CursorPosition.Value;
                if (current is PotentialParseParentNode parent)
                {
                    for (var i = 0; i < cursorPosition; ++i)
                    {
                        result += parent.Children[i].LeafCount;
                    }
                    current = parent.Children[cursorPosition];
                }
                else
                {
                    result += cursorPosition;
                    break;
                }
            }

            return result;
        }

        public static PotentialParseLeafNode GetLeafAtCursorPosition(this PotentialParseNode node) =>
            node is PotentialParseParentNode parent ? parent.Children[node.CursorPosition.Value].GetLeafAtCursorPosition()
                : node.CursorPosition == 0 ? (PotentialParseLeafNode)node
                : throw new InvalidOperationException("trailing cursor");

        public static IEnumerable<PotentialParseLeafNode> GetLeaves(this PotentialParseNode node)
        {
            Invariant.Require(node != null);

            return Traverse.DepthFirst(node, n => n is PotentialParseParentNode parent ? parent.Children : Enumerable.Empty<PotentialParseNode>())
                .OfType<PotentialParseLeafNode>();
        }

        public static int CountNodes(this PotentialParseNode node)
        {
            Invariant.Require(node != null);

            return (node is PotentialParseParentNode parent ? parent.Children.Sum(CountNodes) : 0) + 1;
        }

        public static TNode AdvanceCursor<TNode>(this TNode node)
            where TNode : PotentialParseNode
        {
            Invariant.Require(node.CursorPosition.HasValue && !node.HasTrailingCursor());

            return (TNode)AdvanceOrRemove(node, hasFurtherSiblings: false);

            PotentialParseNode AdvanceOrRemove(PotentialParseNode current, bool hasFurtherSiblings)
            {
                if (current is PotentialParseParentNode parent)
                {
                    var cursorPosition = parent.CursorPosition.Value;
                    var childWithCursor = parent.Children[cursorPosition];

                    // advance or remove the cursor in the child that currently has it
                    var advancedChild = AdvanceOrRemove(childWithCursor, hasFurtherSiblings: hasFurtherSiblings || cursorPosition < parent.Children.Count - 1);

                    // if the child no longer has the cursor, see if we can place it on a sibling
                    if (!advancedChild.CursorPosition.HasValue)
                    {
                        for (var i = cursorPosition + 1; i < parent.Children.Count; ++i)
                        {
                            var child = parent.Children[i];
                            PotentialParseNode newChildWithCursor;
                            if (child.LeafCount > 0)
                            {
                                // found a sibling with leaves
                                newChildWithCursor = child.WithCursor(0);
                            }
                            else if (!hasFurtherSiblings && i == parent.Children.Count - 1)
                            {
                                // no further siblings are coming from parents of this, so take the last sibling with a trailing cursor
                                newChildWithCursor = child.WithTrailingCursor();
                            }
                            else
                            {
                                // neither: just keep going
                                continue;
                            }

                            // replace both the original child and the new cursor-marked child
                            return new PotentialParseParentNode(
                                parent.Rule,
                                parent.Children.Select(ch => ch == childWithCursor ? advancedChild : ch == child ? newChildWithCursor : ch)
                            );
                        }
                    }

                    // if we get here, then either the cursor is still on the same child OR it will be placed
                    // on an outer sibling. Just replace the original cursor child with the advanced version
                    return new PotentialParseParentNode(
                        parent.Rule,
                        parent.Children.Select(ch => ch == childWithCursor ? advancedChild : ch)
                    );
                }

                // if this is the last node, move the cursor to be trailing. Otherwise just remove it
                return hasFurtherSiblings ? current.WithoutCursor() : current.WithTrailingCursor();
            }
        }
    }
}
