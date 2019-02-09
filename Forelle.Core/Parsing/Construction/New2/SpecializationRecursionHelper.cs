using Medallion.Collections;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;

namespace Forelle.Parsing.Construction.New2
{
    internal static class SpecializationRecursionHelper
    {
        public static IEnumerable<ParsingContext> EnumerateSubContexts(ParsingContext context)
        {
            // TODO an open question I have is whether we should bother considering sub-contexts that include
            // top-level nodes. For example, if we have A(B(.B ;)) | A(.B ;), should we consider B(.B ;) vs A(.B ;)?
            // if we decide to support top-level nodes, we need to make sure we don't pick ALL top-level nodes, 
            // we need to change the upfront check, and we need to remove the exclusion in subNodesLists

            // all nodes must have deep structure and no trailing cursor
            if (context.Nodes.Any(n => n.HasTrailingCursor() || !(n.Children[n.CursorPosition.Value] is PotentialParseParentNode)))
            {
                return Enumerable.Empty<ParsingContext>();
            }

            // for each node, build the list of usable sub nodes that could form a sub context
            var subNodesLists = new List<List<PotentialParseParentNode>>(capacity: context.Nodes.Count);
            foreach (var node in context.Nodes)
            {
                var subNodesList = new List<PotentialParseParentNode>();

                var currentNode = node;
                do
                {
                    if (currentNode != node) { subNodesList.Add(currentNode); } // exclude top-level
                    currentNode = currentNode.Children[currentNode.CursorPosition.Value] as PotentialParseParentNode;
                }
                while (currentNode != null);

                subNodesLists.Add(subNodesList);
            }

            var nodesArray = context.Nodes.ToArray();
            return Traverse.DepthFirst(
                    root: (nodes: ImmutableHashSet.Create(context.Nodes.KeyComparer), nextIndex: 0),
                    children: BuildSubContexts
                )
                // todo we should filter out cases where one is a subcontext of the other?
                .Where(t => t.nextIndex == nodesArray.Length)
                .Select(t => new ParsingContext(t.nodes, context.LookaheadTokens));

            IEnumerable<(ImmutableHashSet<PotentialParseParentNode> nodes, int nextIndex)> BuildSubContexts((ImmutableHashSet<PotentialParseParentNode> nodes, int nextIndex) current)
            {
                if (current.nextIndex == nodesArray.Length) { yield break; }

                var subNodesList = subNodesLists[current.nextIndex];
                // enumerate backwards to prefer smaller sub contexts to larger ones
                for (var i = subNodesList.Count - 1; i >= 0; --i)
                {
                    yield return (current.nodes.Add(subNodesList[i]), current.nextIndex + 1);
                }
            }
        }

        /// <summary>
        /// Determines whether <paramref name="subtree"/> appears in <paramref name="supertree"/>
        /// </summary>
        public static bool IsCursorSubtreeOf(this PotentialParseParentNode subtree, PotentialParseParentNode supertree)
        {
            var currentSupertree = supertree;
            do
            {
                if (PotentialParseNodeWithCursorComparer.Instance.Equals(subtree, currentSupertree)) { return true; }

                currentSupertree = currentSupertree.Children[currentSupertree.CursorPosition.Value] as PotentialParseParentNode;
            }
            while (currentSupertree != null);

            return false;
        }

        /// <summary>
        /// Determines whether <paramref name="node"/> contains multiple equivalent expansions
        /// along the path to the cursor
        /// </summary>
        public static bool HasRecursiveExpansion(PotentialParseParentNode node)
        {
            if (node.HasTrailingCursor()) { return false; }

            var rules = new HashSet<(Rule rule, int index)>();
            var current = node;
            do
            {
                var cursorPosition = current.CursorPosition.Value;
                if (!rules.Add((current.Rule, cursorPosition))) { return true; }
                current = current.Children[cursorPosition] as PotentialParseParentNode;
            }
            while (current != null);

            return false;
        }

        /// <summary>
        /// Given <paramref name="node"/> and a <paramref name="subtree"/> occurring within it where the cursor
        /// is on <paramref name="subtree"/>, returns a new <see cref="PotentialParseParentNode"/> where the cursor
        /// is in the first valid position that is not part of <paramref name="subtree"/>.
        /// 
        /// Additionally, <paramref name="subtree"/> is replaced with a <see cref="PotentialParseLeafNode"/> with the same
        /// <see cref="Symbol"/> as <paramref name="subtree"/>.
        /// </summary>
        public static PotentialParseParentNode AdvanceCursorPastSubtree(
            PotentialParseParentNode node, 
            PotentialParseParentNode subtree,
            SubContextPlaceholderSymbolInfo.Factory placeholderFactory)
        {
            Invariant.Require(!node.HasTrailingCursor());
            Invariant.Require(node != subtree);
            Invariant.Require(subtree.CursorPosition.HasValue);
            // this cast will always succeed because by definition a recursive subtree cannot be the whole node
            return (PotentialParseParentNode)AdvanceCursorPastSubtree(node);

            PotentialParseNode AdvanceCursorPastSubtree(PotentialParseParentNode current)
            {
                if (PotentialParseNodeWithCursorComparer.Instance.Equals(current, subtree))
                {
                    return placeholderFactory.GetPlaceholderNode(subtree);
                    //return new PotentialParseLeafNode(current.Symbol, cursorPosition: 1);
                }

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
                    // if the updated child has a trailing cursor, try to place the cursor on a later sibling
                    if (updatedChild.HasTrailingCursor())
                    {
                        // pick the first child after cursor which has a leaf to place the cursor on, or else
                        // the last child after cursor
                        for (var i = cursorPosition + 1; i < current.Children.Count; ++i)
                        {
                            if (i == current.Children.Count - 1 || current.Children[i].LeafCount > 0) { return i; }
                        }
                    }
                    return null;
                }
            }
        }

        /// <summary>
        /// Given a set of non-overlapping <paramref name="partialContexts"/>, returns all distinct 
        /// <see cref="ParsingContext"/>s that could be created by combining them
        /// </summary>
        public static List<ParsingContext> GetAllCombinedContexts(IReadOnlyList<ParsingContext> partialContexts)
        {
            Invariant.Require(
                partialContexts.SelectMany(c => c.Nodes).GroupBy(n => n, PotentialParseNodeWithCursorComparer.Instance).All(g => g.Count() == 1),
                "the contexts should not overlap"
            );

            var result = new List<ParsingContext>();
            for (var i = 0; i < partialContexts.Count; ++i)
            {
                GatherCombinedContexts(partialContexts[i], maxIndexUsed: i);
            }
            return result;

            void GatherCombinedContexts(ParsingContext baseContext, int maxIndexUsed)
            {
                result.Add(baseContext);
                for (var i = maxIndexUsed + 1; i < partialContexts.Count; ++i)
                {
                    var contextToMerge = partialContexts[i];
                    var newContext = new ParsingContext(baseContext.Nodes.Union(contextToMerge.Nodes), baseContext.LookaheadTokens.Union(contextToMerge.LookaheadTokens));
                    GatherCombinedContexts(newContext, i);
                }
            }
        }
    }
}
