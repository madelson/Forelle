using Medallion.Collections;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;

using ParseTreePath = Medallion.Collections.ImmutableLinkedList<(Forelle.Parsing.PotentialParseNode node, int index)>;

namespace Forelle.Parsing.Construction
{
    // todo this code currently doesn't take advantage of the cursor at all, but that could really help by narrowing the
    // possibilities!

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
        private readonly IFirstFollowProvider _firstFollowProvider;
        private readonly ILookup<Symbol, (Rule rule, int index)> _nonDiscriminatorSymbolReferences;

        public AmbiguityContextUnifier(
            IReadOnlyDictionary<NonTerminal, IReadOnlyList<Rule>> rulesByProduced,
            IFirstFollowProvider firstFollowProvider)
        {
            this._rulesByProduced = rulesByProduced;
            this._firstFollowProvider = firstFollowProvider;
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
            if (this.TryUnifyLeaves(nodes, out var unifiedAtLeaves))
            {
                if (this.TryUnifyRoots(unifiedAtLeaves, out var unifiedAtRoots))
                {
                    unified = unifiedAtRoots;
                    return true;
                }

                // even if we can't get root unification, leaf unification is still really helpful
                unified = unifiedAtLeaves;
                return true;
            }

            unified = null;
            return false;
        }

        /// <summary>
        /// The core unification algorithm. Attempts to alter <paramref name="nodes"/> such that each has the
        /// same sequence of <see cref="PotentialParseNode.Leaves"/>
        /// </summary>
        private bool TryUnifyLeaves(IReadOnlyList<PotentialParseNode> nodes, out PotentialParseNode[] unified)
        {
            Invariant.Require(nodes.Count > 1);

            var priorityQueue = new PriorityQueue<UnifyState>(UnifyStateComparer.Instance);
            priorityQueue.Enqueue(new UnifyState(
                nodes.Select(GetFirstPath)
                    .Select(p => new PathUnifyState(
                        p,
                        expansionCount: 0,
                        hasLeafExpansions: false,
                        leafNodeCount: RootOf(p).Leaves.Count
                    ))
                    .ToImmutableArray(),
                progressCount: 0,
                maxPathIndexExpandedAtCurrentProgress: -1
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
                    this.ExpandLeaves(currentState, priorityQueue);
                    this.ExpandRoots(currentState, priorityQueue);
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
                            hasLeafExpansions: oldState.HasLeafExpansions,
                            leafNodeCount: oldState.LeafNodeCount
                        ))
                        .ToImmutableArray(),
                    progressCount: currentState.ProgressCount + 1,
                    // since progress has changed, this resets
                    maxPathIndexExpandedAtCurrentProgress: -1
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
        private void ExpandLeaves(UnifyState currentState, PriorityQueue<UnifyState> priorityQueue)
        {
            for (var i = Math.Max(currentState.MaxPathIndexExpandedAtCurrentProgress, 0); i < currentState.Paths.Length; ++i)
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
                                hasLeafExpansions: true,
                                // we are replacing a leaf with N leaves, so the leaf increase is N - 1.
                                // Since N can be 0, this can yield a net decrease
                                leafNodeCount: pathState.LeafNodeCount + (rule.Symbols.Count - 1)
                            );

                            priorityQueue.Enqueue(new UnifyState(
                                currentState.Paths.SetItem(i, newPathState),
                                progressCount: currentState.ProgressCount,
                                maxPathIndexExpandedAtCurrentProgress: i
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
        private void ExpandRoots(UnifyState currentState, PriorityQueue<UnifyState> priorityQueue)
        {
            // since root expansions require us to reset progress, we don't allow such expansions once
            // progress have been made. We could consider allowing expansions with rules where the reference index
            // is zero, since such rules add leaves only to the end of the tree and therefore would not require a
            // progress reset. However, at this time I don't think attempting this is worth the added complexity
            if (currentState.ProgressCount > 0) { return; }
            
            for (var i = Math.Max(currentState.MaxPathIndexExpandedAtCurrentProgress, 0); i < currentState.Paths.Length; ++i)
            {
                var pathState = currentState.Paths[i];

                // we only allow root expansions when we haven't made leaf expansions yet. This avoids 
                // us generating the same state twice via different expansion sequences
                if (!pathState.HasLeafExpansions)
                {
                    var rootNode = RootOf(pathState.Path);
                    foreach (var reference in this._nonDiscriminatorSymbolReferences[rootNode.Symbol])
                    {
                        var newRootNode = new PotentialParseParentNode(
                            reference.rule,
                            reference.rule.Symbols.Select((s, index) => index == reference.index ? rootNode : new PotentialParseLeafNode(s))
                        );
                        var newPathState = new PathUnifyState(
                            GetFirstPath(newRootNode),
                            expansionCount: pathState.ExpansionCount + 1,
                            hasLeafExpansions: pathState.HasLeafExpansions, // will be false (see check above)
                            // in the new root, all symbols are leaf nodes except where we slot in the old root. Thus, the increase
                            // in leaf nodes is one less than the number of rule symbols. Because a reference rule must have at least
                            // one symbol, this always increases the leaf node count
                            leafNodeCount: pathState.LeafNodeCount + (reference.rule.Symbols.Count - 1)
                        );
                        priorityQueue.Enqueue(new UnifyState(
                            currentState.Paths.SetItem(i, newPathState),
                            progressCount: currentState.ProgressCount, // will be 0 (see check above)
                            maxPathIndexExpandedAtCurrentProgress: i
                        ));
                    }
                }
            }
        }

        /// <summary>
        /// Once we've unified all leaves, we can further clarify the situation by unifying the output so that
        /// the root node of each path is the same symbol. This makes it obvious what the parser was looking at when
        /// it encountered ambiguity
        /// </summary>
        private bool TryUnifyRoots(IReadOnlyList<PotentialParseNode> unifiedAtLeaves, out PotentialParseNode[] unifiedAtRoots)
        {
            // short-circuit if roots are already unified
            if (unifiedAtLeaves.Select(n => n.Symbol).Distinct().Count() == 1)
            {
                unifiedAtRoots = unifiedAtLeaves.ToArray();
                return true;
            }

            var expansions = unifiedAtLeaves.Select(
                    n => this.GetUniqueRootExpansionsAddingNoLeaves(n)
                        .GroupBy(e => e.Symbol)
                        // avoid considering symbols which can be reached via multiple expansion paths
                        // since there's no way to choose the "right" path
                        .Where(g => g.Count() == 1)
                        .ToDictionary(g => g.Key, g => g.Single())
                )
                .ToArray();

            // find all symbols which all nodes have an expansion for
            var unifyingRootSymbols = expansions[0].Keys
                .Where(s => expansions.Skip(1).All(e => e.ContainsKey(s)))
                .ToArray();
            if (unifyingRootSymbols.Length == 0)
            {
                unifiedAtRoots = null;
                return false;
            }

            // we could pick any unifying root symbol, but the one with the fewest total nodes 
            // should be the simplest to look at and ensures consistency
            var bestUnifyingRootSymbol = unifyingRootSymbols.OrderBy(s => expansions.Sum(e => CountNodes(e[s])))
                // break ties using name for consistency
                .ThenBy(s => s.Name)
                .First();

            unifiedAtRoots = expansions.Select(e => e[bestUnifyingRootSymbol]).ToArray();
            return true;
        }

        private List<PotentialParseNode> GetUniqueRootExpansionsAddingNoLeaves(PotentialParseNode startingNode)
        {
            var results = new List<PotentialParseNode>();
            GatherExpansions(startingNode, ImmutableHashSet<NonTerminal>.Empty);
            return results;

            void GatherExpansions(PotentialParseNode node, ImmutableHashSet<NonTerminal> usedSymbols)
            {
                results.Add(node);

                var references = this._nonDiscriminatorSymbolReferences[node.Symbol]
                    // avoid infinite recursion
                    .Where(r => !usedSymbols.Contains(r.rule.Produced))
                    // must add no leaf symbols, so all symbols other than where we'll be plugging in node must be nullable
                    .Where(r => !r.rule.Symbols.Where((s, i) => i != r.index && !this._firstFollowProvider.IsNullable(s)).Any())
                    .GroupBy(r => r.rule.Produced)
                    // there must be only one reference producing the current symbol, since otherwise we don't know which to pick
                    // and picking just one would be wrong (this is the "Unique") part of the method name
                    .Where(g => g.Count() == 1)
                    .Select(g => g.Single());
                foreach (var reference in references)
                {
                    var newNode = new PotentialParseParentNode(
                        reference.rule,
                        reference.rule.Symbols.Select((s, i) => i == reference.index ? node : this.EmptyParseOf((NonTerminal)s))
                    );
                    GatherExpansions(newNode, usedSymbols.Add(newNode.Symbol));
                }
            }
        }

        private PotentialParseParentNode EmptyParseOf(NonTerminal produced)
        {
            return new PotentialParseParentNode(
                this._rulesByProduced[produced].Single(r => r.Symbols.Count == 0),
                Enumerable.Empty<PotentialParseNode>()
            );
        }

        private static int CountNodes(PotentialParseNode node)
        {
            return node is PotentialParseParentNode parent ? 1 + parent.Children.Sum(CountNodes)
                : node is PotentialParseLeafNode leaf ? 1
                : throw new ArgumentException("Unexpected node type");
        }

        #region ---- States ----
        private class UnifyState
        {
            public UnifyState(
                ImmutableArray<PathUnifyState> paths,
                int progressCount,
                int maxPathIndexExpandedAtCurrentProgress)
            {
                Invariant.Require(maxPathIndexExpandedAtCurrentProgress >= -1 && maxPathIndexExpandedAtCurrentProgress < paths.Length);

                this.Paths = paths;
                this.ProgressCount = progressCount;
                this.MaxPathIndexExpandedAtCurrentProgress = maxPathIndexExpandedAtCurrentProgress;
            }

            public ImmutableArray<PathUnifyState> Paths { get; }
            /// <summary>
            /// The number of leaves we've bypassed thus far across all paths
            /// </summary>
            public int ProgressCount { get; }
            /// <summary>
            /// The max index in <see cref="Paths"/> which has been expanded since <see cref="ProgressCount"/> last changed
            /// or -1 if no paths have been expanded.
            /// 
            /// The purpose of tracking this is to allow us to avoid generating the same state via different expansion
            /// sequences. For example, we might expand path 0 and then path 3 or vice-versa. We avoid this by disallowing
            /// expansions of paths lower than this index so that once path 3 is expanded we can't go back and expand 0, 1, or 2
            /// until we make progress.
            /// </summary>
            public int MaxPathIndexExpandedAtCurrentProgress { get; }
            
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
                bool hasLeafExpansions,
                int leafNodeCount)
            {
                Invariant.Require(!hasLeafExpansions || expansionCount > 0);

                this.Path = path;
                this.ExpansionCount = expansionCount;
                this.HasLeafExpansions = hasLeafExpansions;
                this.LeafNodeCount = leafNodeCount;
            }

            public ImmutableLinkedList<(PotentialParseNode node, int index)> Path { get; }
            public int ExpansionCount { get; }
            public bool HasLeafExpansions { get; }
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
