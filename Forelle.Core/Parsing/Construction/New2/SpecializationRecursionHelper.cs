using Medallion.Collections;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Forelle.Parsing.Construction.New2
{
    internal static class SpecializationRecursionHelper
    {
        /// <summary>
        /// Given the path of expanded rules from the cursor of <paramref name="node"/>, identifies the longest
        /// pattern A B C... that repeats. This indicates that recursive expansion has occurred
        /// </summary>
        public static PotentialParseParentNode GetRecursiveSubtreeOrDefault(PotentialParseParentNode node)
        {
            if (node.HasTrailingCursor()) { return null; }

            var subtreeParts = GetSubtreePartsFromRoot(node);
            subtreeParts.Reverse();
            
            var maxRecurrenceLength = 0;
            while (HasRecurrence(maxRecurrenceLength + 1)) { ++maxRecurrenceLength; }

            return maxRecurrenceLength > 0
                ? subtreeParts[maxRecurrenceLength - 1].Node 
                : null;
            
            bool HasRecurrence(int recurrenceLength)
            {
                // note: we start searching from recurrenceLength because otherwise any recurrence we find would overlap the pattern
                // at the beginning. Given the condition below which does not allow internal repetition within a recurrence, an overlapping
                // pattern like this can never succeed
                for (var recurrenceStart = recurrenceLength; recurrenceStart < subtreeParts.Count - (recurrenceLength - 1); ++recurrenceStart)
                {
                    if (IsRecurrence()) { return true; }

                    bool IsRecurrence()
                    {
                        for (var i = 0; i < recurrenceLength; ++i)
                        {
                            if (!subtreeParts[i].Equals(subtreeParts[recurrenceStart + i])
                                // if the recurrence itself recurs, then reject it. For example, if we find A B A as
                                // a repeating pattern, we want to just identify A B instead since that is the minimal unit
                                // that repeats. Note that we will allow A B B, despite B technically being a recurrence
                                // because this only comes up if we earlier found B but couldn't use it because another path
                                // was not yet recursive
                                || (i > 0 && subtreeParts[0].Equals(subtreeParts[i])))
                            {
                                return false;
                            }
                        }

                        return true;
                    }
                }

                return false;
            }
        }

        private static List<SubtreePart> GetSubtreePartsFromRoot(PotentialParseParentNode node)
        {
            var result = new List<SubtreePart>();
            PotentialParseNode current = node;
            while (current is PotentialParseParentNode parent)
            {
                result.Add(new SubtreePart(parent));
                current = parent.Children[parent.CursorPosition.Value];
            }

            return result;
        }

        /// <summary>
        /// Provides equality based on (a) the <see cref="Rule"/> being expanded, (b)
        /// the <see cref="PotentialParseNode.CursorPosition"/>, and (c) the
        /// <see cref="PotentialParseParentNode.Children"/> occurring before the cursor
        /// </summary>
        private readonly struct SubtreePart : IEquatable<SubtreePart>
        {
            public SubtreePart(PotentialParseParentNode node)
            {
                this.Node = node;
            }

            public PotentialParseParentNode Node { get; }

            public bool Equals(SubtreePart other)
            {
                if (other.Node.Rule != this.Node.Rule
                    || other.Node.CursorPosition != this.Node.CursorPosition)
                {
                    return false;
                }

                for (var i = 0; i < this.Node.CursorPosition.Value; ++i)
                {
                    if (!PotentialParseNode.Comparer.Equals(this.Node.Children[i], other.Node.Children[i]))
                    {
                        return false;
                    }
                }

                return true;
            }

            public override int GetHashCode()
            {
                var hash = this.Node.Rule.GetHashCode();
                for (var i = 0; i < this.Node.CursorPosition.Value; ++i)
                {
                    hash = (hash, PotentialParseNode.Comparer.GetHashCode(this.Node.Children[i])).GetHashCode();
                }
                return hash;
            }
        }

        /// <summary>
        /// Given <paramref name="node"/> and a <paramref name="subtree"/> occurring within it where the cursor
        /// is on <paramref name="subtree"/>, returns a new <see cref="PotentialParseParentNode"/> where the cursor
        /// is in the first valid position that is not part of <paramref name="subtree"/>
        /// </summary>
        public static PotentialParseParentNode AdvanceCursorPastSubtree(PotentialParseParentNode node, PotentialParseParentNode subtree)
        {
            Invariant.Require(!node.HasTrailingCursor());
            Invariant.Require(subtree.CursorPosition.HasValue);
            return AdvanceCursorPastSubtree(node);

            PotentialParseParentNode AdvanceCursorPastSubtree(PotentialParseParentNode current)
            {
                if (current == subtree) { return current.WithoutCursor().WithTrailingCursor(); }

                var cursorPosition = current.CursorPosition.Value;
                var childWithCursor = current.Children[cursorPosition];
                var updatedChild = AdvanceCursorPastSubtree((PotentialParseParentNode)childWithCursor);
                var newCursorIndex = GetNewCursorIndex();

                return new PotentialParseParentNode(
                    current.Rule,
                    current.Children.Select((ch, i) =>
                    {
                        if (i == cursorPosition)
                        {
                            return newCursorIndex.HasValue ? updatedChild.WithoutCursor() : updatedChild;
                        }
                        if (i == newCursorIndex)
                        {
                            return current.Children[i].WithCursor(0);
                        }
                        return current.Children[i];
                    })
                );

                int? GetNewCursorIndex()
                {
                    // pick the first child after cursor which has a leaf to place the cursor on, or else
                    // the last child after cursor
                    for (var i = cursorPosition + 1; i < current.Children.Count; ++i)
                    {
                        if (i == current.Children.Count - 1 || current.Children[i].LeafCount > 0) { return i; }
                    }
                    return null;
                }
            }
        }
    }
}
