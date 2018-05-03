using Medallion.Collections;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;

using ParseTreePath = Medallion.Collections.ImmutableLinkedList<(Forelle.Parsing.PotentialParseNode node, int index)>;

namespace Forelle.Parsing.Construction
{
    // todo can avoid duplicate work here by not expanding index 1, then 2 and also 2, then 1

    /// <summary>
    /// The <see cref="AmbiguityContextUnifier"/>'s job is to take two or more <see cref="PotentialParseNode"/>s representing
    /// ambiguous points in a grammar and attempt to "unify" them. That means performing grammatically-valid transformations on
    /// some or all of the nodes until the sequence of <see cref="PotentialParseLeafNode"/>s is the same across all trees.
    /// 
    /// The point of this is to make the ambiguous situation as obvious as possible and therefore easy for the parser designer
    /// to resolve.
    /// </summary>
    internal class AmbiguityContextUnifier
    {
        private readonly IReadOnlyDictionary<NonTerminal, IReadOnlyList<Rule>> _rulesByProduced;
        private readonly ILookup<Symbol, (Rule rule, int index)> _nonDiscriminatorSymbolReferences;

        public AmbiguityContextUnifier(IReadOnlyDictionary<NonTerminal, IReadOnlyList<Rule>> rulesByProduced)
        {
            this._rulesByProduced = rulesByProduced;
            // TODO rationalize with similar build of this in AmbContextualizer
            this._nonDiscriminatorSymbolReferences = this._rulesByProduced.Where(kvp => !(kvp.Key.SyntheticInfo is DiscriminatorSymbolInfo))
                .SelectMany(kvp => kvp.Value)
                .SelectMany(r => r.Symbols.Select((s, i) => (referenced: s, index: i, rule: r)))
                .ToLookup(t => t.referenced, t => (rule: t.rule, index: t.index));
        }

        /// <summary>
        /// Used to keep the algorithm from hanging forever
        /// </summary>
        private const int MaxExpansionCount = 20;

        public bool TryUnify(IReadOnlyList<PotentialParseNode> nodes, out PotentialParseNode[] unified)
        {
            Invariant.Require(nodes.Count > 1);

            var priorityQueue = new PriorityQueue<UnifyState>(UnifyStateComparer.Instance);
            priorityQueue.Enqueue(new UnifyState(
                nodes.Select(GetFirstPath)
                    .Select(p => new PathUnifyState(
                        p,
                        expansionCount: 0,
                        leafNodeCount: RootOf(p).Leaves.Count
                    ))
                    .ToImmutableArray(),
                progressCount: 0,
                canHaveRootExpansions: true
            ));

            while (priorityQueue.Count > 0)
            {
                var currentState = priorityQueue.Dequeue();

                // see if the paths can be advanced or have all finished
                if (this.TryAdvanceAllPaths(currentState, priorityQueue))
                {
                    unified = currentState.Paths.Select(p => RootOf(p.Path)).ToArray();
                    return true;
                }

                if (currentState.TotalExpansionCount < MaxExpansionCount)
                {
                    this.ExpandInner(currentState, priorityQueue);
                    this.ExpandOuter(currentState, priorityQueue);
                }
            }

            unified = null;
            return false;
        }
        
        private bool TryAdvanceAllPaths(UnifyState currentState, PriorityQueue<UnifyState> priorityQueue)
        {
            var currentPaths = currentState.Paths;

            // see if all symbols match: if so, we can advance all paths
            Symbol commonSymbol = null;
            foreach (var pathState in currentState.Paths)
            {
                if (commonSymbol == null) { commonSymbol = pathState.Path.Head.node.Symbol; }
                else if (commonSymbol != pathState.Path.Head.node.Symbol) { return false; }
            }

            // try to advance each path
            ParseTreePath[] advancedPaths = null;
            var index = 0;
            foreach (var pathState in currentState.Paths)
            {
                if (TryGetNext(pathState.Path, out var next))
                {
                    if (advancedPaths == null)
                    {
                        // first path is a success
                        if (index == 0) { advancedPaths = new ParseTreePath[currentState.Paths.Length]; }
                        // first success after previous failures
                        else { return false; }
                    }
                    advancedPaths[index] = next; // all successful so far
                }
                else if (advancedPaths != null)
                {
                    return false; // first failure after previous successes
                }

                ++index;
            }

            // advanced all
            if (advancedPaths != null)
            {
                priorityQueue.Enqueue(new UnifyState(
                    currentPaths.Select((oldState, i) => new PathUnifyState(
                            path: advancedPaths[i],
                            expansionCount: oldState.ExpansionCount,
                            leafNodeCount: oldState.LeafNodeCount
                        ))
                        .ToImmutableArray(),
                    progressCount: currentState.ProgressCount + 1,
                    // disallow root expansions once we've made progress
                    canHaveRootExpansions: false
                ));
                return false; // didn't finish
            }

            // advanced none: finished if none equivalent.
            // It's possible to find equivalent expansions. However, this can't be the ambiguous
            // case so just ignore it
            var distinctNodeCount = currentState.Paths.Select(p => RootOf(p.Path))
                .Distinct(PotentialParseNode.Comparer)
                .Count();
            return distinctNodeCount == currentState.Paths.Length;
        }
        
        /// <summary>
        /// For each path in the <paramref name="currentState"/> that ends in a <see cref="NonTerminal"/>,
        /// expands the path using all <see cref="Rule"/>s for that <see cref="Symbol"/>
        /// </summary>
        private void ExpandInner(UnifyState currentState, PriorityQueue<UnifyState> priorityQueue)
        {
            for (var i = 0; i < currentState.Paths.Length; ++i)
            {
                var pathState = currentState.Paths[i];
                if (pathState.Path.Head.node.Symbol is NonTerminal nonTerminal)
                {
                    foreach (var rule in this._rulesByProduced[nonTerminal])
                    {
                        if (TryGetPathAfterExpansion(
                            pathState.Path,
                            rule,
                            out var expandedPath))
                        {
                            var newPathState = new PathUnifyState(
                                expandedPath,
                                pathState.ExpansionCount + 1,
                                // we are replacing a leaf with N leaves, so the leaf increase is N - 1.
                                // Since N can be 0, this can yield a net decrease
                                leafNodeCount: pathState.LeafNodeCount + (rule.Symbols.Count - 1)
                            );

                            priorityQueue.Enqueue(new UnifyState(
                                currentState.Paths.SetItem(i, newPathState),
                                progressCount: currentState.ProgressCount,
                                // disallow root expansions once we've made an internal expansion
                                canHaveRootExpansions: false
                            ));
                        }
                    }
                }
            }
        }

        /// <summary>
        /// For each path from <paramref name="currentState"/>, considers expanding the path using
        /// other rules that reference the <see cref="Symbol"/> of the path's root <see cref="PotentialParseNode"/>
        /// </summary>
        private void ExpandOuter(UnifyState currentState, PriorityQueue<UnifyState> priorityQueue)
        {
            if (!currentState.CanHaveRootExpansions) { return; }
            
            for (var i = 0; i < currentState.Paths.Length; ++i)
            {
                var rootNode = RootOf(currentState.Paths[i].Path);
                foreach (var reference in this._nonDiscriminatorSymbolReferences[rootNode.Symbol])
                {
                    var newRootNode = new PotentialParseParentNode(
                        reference.rule,
                        reference.rule.Symbols.Select((s, index) => index == reference.index ? rootNode : new PotentialParseLeafNode(s))
                    );
                    var newPathState = new PathUnifyState(
                        GetFirstPath(newRootNode),
                        expansionCount: currentState.Paths[i].ExpansionCount + 1,
                        leafNodeCount: newRootNode.Leaves.Count
                    );
                    priorityQueue.Enqueue(new UnifyState(
                        currentState.Paths.SetItem(i, newPathState),
                        progressCount: 0,
                        canHaveRootExpansions: true
                    ));
                }
            }
        }

        #region ---- States ----
        private class UnifyState
        {
            public UnifyState(
                ImmutableArray<PathUnifyState> paths,
                int progressCount,
                bool canHaveRootExpansions)
            {
                this.Paths = paths;
                this.ProgressCount = progressCount;
                this.CanHaveRootExpansions = canHaveRootExpansions;
            }

            public ImmutableArray<PathUnifyState> Paths { get; }
            public int ProgressCount { get; }
            public bool CanHaveRootExpansions { get; }

            private int _cachedTotalExpansionCount = -1;

            public int TotalExpansionCount
            {
                get
                {
                    if (this._cachedTotalExpansionCount < 0)
                    {
                        this._cachedTotalExpansionCount = 0;
                        foreach (var path in this.Paths) { this._cachedTotalExpansionCount += path.ExpansionCount; }
                    }
                    return this._cachedTotalExpansionCount;
                }
            }

            private int _cachedTotalLeafNodeCount = -1;

            public int TotalLeafNodeCount
            {
                get
                {
                    if (this._cachedTotalLeafNodeCount < 0)
                    {
                        this._cachedTotalLeafNodeCount = 0;
                        foreach (var path in this.Paths) { this._cachedTotalLeafNodeCount += path.LeafNodeCount; }
                    }
                    return this._cachedTotalLeafNodeCount;
                }
            }
        }

        private class PathUnifyState
        {
            public PathUnifyState(
                ParseTreePath path,
                int expansionCount,
                int leafNodeCount)
            {
                this.Path = path;
                this.ExpansionCount = expansionCount;
                this.LeafNodeCount = leafNodeCount;
            }

            public ImmutableLinkedList<(PotentialParseNode node, int index)> Path { get; }
            public int ExpansionCount { get; }
            public int LeafNodeCount { get; }
        }

        private class UnifyStateComparer : IComparer<UnifyState>
        {
            public static UnifyStateComparer Instance = new UnifyStateComparer();

            public int Compare(UnifyState x, UnifyState y)
            {
                // first prefer fewest total expansions
                var expansionCountComparison = x.TotalExpansionCount.CompareTo(y.TotalExpansionCount);
                if (expansionCountComparison != 0) { return expansionCountComparison; }

                // next prefer smallest remaining count
                var remainingCountComparison = GetRemainingCount(x).CompareTo(GetRemainingCount(y));
                if (remainingCountComparison != 0) { return remainingCountComparison; }

                // next prefer fewer leaves
                var leafCountComparison = x.TotalLeafNodeCount.CompareTo(y.TotalLeafNodeCount);
                if (leafCountComparison != 0) { return leafCountComparison; }

                return 0;
            }

            private static int GetRemainingCount(UnifyState state) => state.TotalLeafNodeCount - (state.Paths.Length * state.ProgressCount);
        }
        #endregion

        #region ---- "Path" Utilities ----

        // a "path" is a traversal list from leaf to root of a potential parse node tree

        private static PotentialParseNode RootOf(ParseTreePath path)
        {
            Invariant.Require(path.Count > 0);

            PotentialParseNode last = null;
            foreach (var (node, index) in path)
            {
                last = node;
            }
            return last;
        }

        /// <summary>
        /// Given a <paramref name="current"/> path, finds returns the <paramref name="next"/> path in a forward traversal of the parse tree
        /// if there is one
        /// </summary>
        private static bool TryGetNext(ParseTreePath current, out ParseTreePath next)
        {
            var (head, tail) = current; // always works: a valid path cannot be empty

            if (tail.Count > 0) // if we have a parent...
            {
                // ...try expanding later children
                var parent = (PotentialParseParentNode)tail.Head.node;
                for (var i = head.index + 1; i < parent.Children.Count; ++i)
                {
                    if (TryExpandFirstPath(tail.Prepend((node: parent.Children[i], index: i)), out next))
                    {
                        return true;
                    }
                }

                // otherwise recurse on the parent's parent
                return TryGetNext(tail, out next);
            }

            next = default;
            return false;
        }

        /// <summary>
        /// Returns the path starting from the first leaf node of the parse tree
        /// </summary>
        private static ParseTreePath GetFirstPath(PotentialParseNode node)
        {
            Invariant.Require(
                TryExpandFirstPath(ImmutableLinkedList.Create((node, index: 0)), out var result),
                "Empty parse node"
            );

            return result;
        }

        /// <summary>
        /// Given a path starting from a node in a tree, expands out the path to the first
        /// leaf node under that path (the result may be the <paramref name="basePath"/>)
        /// </summary>
        private static bool TryExpandFirstPath(
            ParseTreePath basePath,
            out ParseTreePath result)
        {
            var (head, tail) = basePath;
            if (head.node is PotentialParseParentNode parent)
            {
                for (var i = 0; i < parent.Children.Count; ++i)
                {
                    if (TryExpandFirstPath(basePath.Prepend((node: parent.Children[i], index: i)), out result))
                    {
                        return true;
                    }
                }

                result = default;
                return false;
            }

            result = basePath;
            return true;
        }

        private static bool TryGetPathAfterExpansion(
            ParseTreePath path,
            Rule rule,
            out ParseTreePath nextPath)
        {
            Invariant.Require(path.Head.node is PotentialParseLeafNode);

            // build a new path replacing the leaft with a default parse of the rule
            var newPath = ReplaceLeafNode(path, PotentialParseNode.Create(rule));

            // if the rule has children, then we will return a path pointing to the first child
            if (rule.Symbols.Count > 0)
            {
                nextPath = newPath.Prepend((node: ((PotentialParseParentNode)newPath.Head.node).Children[0], index: 0));
                return true;
            }

            // if the rule does not have children, then we need to search for the next valid path
            return TryGetNext(newPath, out nextPath);
        }

        private static ParseTreePath ReplaceLeafNode(
            ParseTreePath path,
            PotentialParseNode replacement)
        {
            var (head, tail) = path;
            Invariant.Require(head.node.Symbol == replacement.Symbol);

            // base case
            if (tail.Count == 0)
            {
                return ImmutableLinkedList.Create((node: replacement, index: 0));
            }

            // recursive case: compute an expanded parent and expand tail as that
            var parent = (PotentialParseParentNode)tail.Head.node;
            var expandedParent = new PotentialParseParentNode(
                parent.Rule,
                parent.Children.Select((ch, index) => index == head.index ? replacement : ch)
            );
            return ReplaceLeafNode(tail, expandedParent).Prepend((node: replacement, head.index));
        }
        #endregion
    }
}
