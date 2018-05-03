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

        public static PotentialParseNode WithCursor(this PotentialParseNode node, int cursorPosition)
        {
            if (node.CursorPosition == cursorPosition) { return node; }

            if (node is PotentialParseParentNode parent)
            {
                if (parent.Children.Count == 0)
                {
                    Invariant.Require(cursorPosition == 0);
                    return new PotentialParseParentNode(parent.Rule, hasTrailingCursor: true);
                }

                Invariant.Require(cursorPosition >= 0 && cursorPosition <= parent.Children.Count);
                var result = new PotentialParseParentNode(
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
                return result;
            }

            return new PotentialParseLeafNode(node.Symbol, cursorPosition);
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
    }
}
