using Medallion.Collections;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;

namespace Forelle.Parsing.Construction
{
    // TODO AMB we aren't currently correctly handling the case where the cursor rests on a non-terminal in both nodes and we just move on past that; we should probably (a) block
    // alignment in this case (b) handle this first thing after resolving trailing cursors (c) handle via a transform much like what ExpandFirstContexts does today in AmbiguityContextualizer
    // to find all methods of expanding that leaf / subsequent leaves. ONCE we have this, we can stop doing ExpandFirst/ExpandFollow in AmbiguityContextualizer

    // TODO AMB extensive comments

    // unify PAIRS of nodes, not N nodes. This avoids needing to find an N-way ambiguity, and also simplifies the problem. The consumer must resolve in a consistent manner

    // starting at the cursor, unify leaves after the cursor to the end, then proceeding the cursor to the beginning. Finally, unify roots if not unified
    // note that as we do any of these steps, we may disrupt any previous steps (due to changing the root!)

    // to unify at a given position, for each node:
    // (a) with a token at position, we check to see if any node does not have the token in the NextOf/LastOf set. If so, we're stuck => dead end
    // (b) with a non-terminal at position, we consider all rules to expand that non-terminal
    // (c) with nothing at position, we consider all rules to expand the root. Since we've eliminated left-recursion, we don't have to worry about looping forever here without doing anything

    // to unify at the root

    // additionally, note that when considering any given position (or root), we will track the highest expanded node index and only consider expansions for nodes with the same or higher index. The reason
    // is to avoid duplicate work where 2 search paths re-converge

    // we build a PQ of our search states

    internal class AmbiguityContextUnifier2
    {
        /// <summary>
        /// Used to keep the algorithm from hanging forever, especially in cases where there are an unbounded number of expansions that we could try
        /// </summary>
        private const int MaxExpansionCount = 20;

        private readonly IReadOnlyDictionary<NonTerminal, IReadOnlyList<Rule>> _rulesByProduced;
        private readonly FirstFollowLastPrecedingCalculator _firstFollowLastPreceding;
        private readonly ILookup<Symbol, (Rule rule, int index)> _nonDiscriminatorSymbolReferences;

        public AmbiguityContextUnifier2(IReadOnlyDictionary<NonTerminal, IReadOnlyList<Rule>> rulesByProduced)
        {
            this._rulesByProduced = rulesByProduced;

            var nonDiscriminatorRules = this._rulesByProduced.Where(kvp => !(kvp.Key.SyntheticInfo is DiscriminatorSymbolInfo))
                .SelectMany(kvp => kvp.Value)
                .ToArray();

            this._firstFollowLastPreceding = FirstFollowLastPrecedingCalculator.Create(nonDiscriminatorRules);

            // TODO AMB rationalize with similar build of this in AmbContextualizer
            this._nonDiscriminatorSymbolReferences = nonDiscriminatorRules
                .SelectMany(r => r.Symbols.Select((s, i) => (referenced: s, index: i, rule: r)))
                .ToLookup(t => t.referenced, t => (rule: t.rule, index: t.index));
        }

        public bool TryUnify(
            IReadOnlyCollection<PotentialParseParentNode> firsts,
            IReadOnlyCollection<PotentialParseParentNode> seconds,
            Token lookahead,
            out PotentialParseParentNode unifiedFirst,
            out PotentialParseParentNode unifiedSecond)
        {
            Invariant.Require(new[] { firsts, seconds }.All(n => n.Any()), "each set must have at least one node");

            var priorityQueue = new PriorityQueue<SearchState>();
            priorityQueue.EnqueueRange(GetInitialSearchStates(firsts, seconds));

            do
            {
                // get the current state
                var currentState = priorityQueue.Dequeue();
                
                // if the current state is unified, we're done
                if (!currentState.HasAnyTrailingCursor
                    && !currentState.HasUnmatchedLeadingSymbols
                    && !currentState.HasUnmatchedTrailingSymbols
                    && !currentState.HasMismatchedRoots)
                {
                    unifiedFirst = currentState.First.Node;
                    unifiedSecond = currentState.Second.Node;
                    return true;
                }

                if (!this.IsDeadEnd(currentState, lookahead))
                {
                    // explore states adjacent to the current state
                    var nextSearchStates = GetNextSearchStates(currentState, lookahead);
                    priorityQueue.EnqueueRange(nextSearchStates);
                }
            }
            while (priorityQueue.Count > 0);

            unifiedFirst = unifiedSecond = null;
            return false;
        }

        private static IEnumerable<SearchState> GetInitialSearchStates(
            IReadOnlyCollection<PotentialParseParentNode> firsts, 
            IReadOnlyCollection<PotentialParseParentNode> seconds)
        {
            var secondStates = seconds.Select(n => new NodeState(n)).ToArray();

            return from firstState in firsts.Select(n => new NodeState(n))
                   from secondState in secondStates
                   select new SearchState(firstState, secondState, new SearchContext(SearchAction.Initial, cursorRelativeLeafIndex: 0, expandedSecond: false));
        }

        private IEnumerable<SearchState> GetNextSearchStates(SearchState state, Token lookahead)
        {
            if (state.HasAnyTrailingCursor)
            {
                return this.ExpandRootToRemoveTrailingCursor(state, lookahead);
            }

            if (state.HasUnmatchedTrailingSymbols)
            {
                return this.ExpandLeafToResolveUnmatchedTrailingSymbols(state);
            }

            if (state.HasUnmatchedLeadingSymbols)
            {
                return this.ExpandLeafToResolveUnmatchedLeadingSymbols(state);
            }

            if (state.HasMismatchedRoots)
            {
                return this.ExpandRootToResolveMismatchedRoots(state);
            }

            throw new InvalidOperationException("should never get here");
        }

        private bool IsDeadEnd(SearchState state, Token lookahead)
        {
            return state.TotalExpansionCount == MaxExpansionCount
                || IsDeadEndBasedOnTokenMatching(state.First, state.Second)
                || IsDeadEndBasedOnTokenMatching(state.Second, state.First);

            bool IsDeadEndBasedOnTokenMatching(NodeState a, NodeState b)
            {
                // trailing check
                var requiredTrailingToken = a.HasTrailingCursor ? lookahead
                    : a.TrailingUnmatchedSymbols.TryDeconstruct(out var nextTrailing, out _) && nextTrailing is Token nextTrailingToken ? nextTrailingToken
                    : null;
                if (requiredTrailingToken != null)
                {
                    if (b.HasTrailingCursor)
                    {
                        if (requiredTrailingToken != lookahead) { return true; }
                    }
                    else
                    {
                        var canHaveTokenInTrailing = CanHaveToken(b.TrailingUnmatchedSymbols, requiredTrailingToken, (p, s) => p.FirstOf(s));
                        if (!(canHaveTokenInTrailing ?? this._firstFollowLastPreceding.FollowOf(b.Node.Rule.Produced).Contains(requiredTrailingToken)))
                        {
                            return true;
                        }
                    }
                }

                // leading check
                var requiredLeadingToken = a.LeadingUnmatchedSymbols.TryDeconstruct(out var nextLeading, out _) && nextLeading is Token nextLeadingToken
                    ? nextLeadingToken
                    : null;
                if (requiredLeadingToken != null)
                {
                    var canHaveTokenInLeading = CanHaveToken(b.LeadingUnmatchedSymbols, requiredLeadingToken, (p, s) => p.LastOf(s));
                    if (!(canHaveTokenInLeading ?? this._firstFollowLastPreceding.PrecedingOf(b.Node.Rule.Produced).Contains(requiredLeadingToken)))
                    {
                        return true;
                    }
                }

                return false;
            }

            bool? CanHaveToken(ImmutableLinkedList<Symbol> symbols, Token token, Func<FirstFollowLastPrecedingCalculator, Symbol, ImmutableHashSet<Token>> setFunc)
            {
                foreach (var symbol in symbols)
                {
                    var set = setFunc(this._firstFollowLastPreceding, symbol);
                    if (set.Contains(token)) { return true; }
                    if (!set.Contains(null)) { return false; }
                }

                return null;
            }
        }

        private IEnumerable<SearchState> ExpandRootToRemoveTrailingCursor(SearchState state, Token lookahead)
        {
            if (state.First.HasTrailingCursor)
            {
                var context = new SearchContext(SearchAction.RemoveTrailingCursor, cursorRelativeLeafIndex: 0, expandedSecond: false);
                Invariant.Require(state.Context.CanBeFollowedWith(context)); // the outer else check should ensure this

                return ExpandRootToRemoveTrailingCursor(state.First)
                    .Select(newFirst => new SearchState(newFirst, state.Second, context));
            }
            // given that trailing cursors must be dealt with and that we insist on expanding first before second, we only try to resolve
            // second's trailing cursor if first doesn't have one
            else
            {
                var context = new SearchContext(SearchAction.RemoveTrailingCursor, cursorRelativeLeafIndex: 0, expandedSecond: true);
                Invariant.Require(state.Context.CanBeFollowedWith(context)); // should always be true, since we're expanding second

                return ExpandRootToRemoveTrailingCursor(state.Second)
                    .Select(newSecond => new SearchState(state.First, newSecond, context));
            }

            IEnumerable<NodeState> ExpandRootToRemoveTrailingCursor(NodeState nodeState)
            {
                var usableReferences = this._nonDiscriminatorSymbolReferences[nodeState.Node.Symbol]
                    .Where(reference => this._firstFollowLastPreceding.NextOfContains(reference.rule.Skip(reference.index + 1), lookahead));
                return usableReferences.Select(reference => nodeState.ExpandRoot(reference.rule, reference.index));
            }
        }

        private IEnumerable<SearchState> ExpandLeafToResolveUnmatchedTrailingSymbols(SearchState state)
        {
            Invariant.Require(!state.HasAnyTrailingCursor);

            var firstExpansions = ExpandLeafToResolveUnmatchedTrailingSymbols(state.First);
            var secondExpansions = ExpandLeafToResolveUnmatchedTrailingSymbols(state.Second);

            return firstExpansions.Concat(secondExpansions)
                .Select(t => ToSearchState(state, t.expanded, t.context));

            IEnumerable<(NodeState expanded, SearchContext context)> ExpandLeafToResolveUnmatchedTrailingSymbols(NodeState nodeState)
            {
                if (nodeState.TrailingUnmatchedSymbols.Count == 0)
                {
                    var context = new SearchContext(
                        SearchAction.RemoveUnmatchedLeadingSymbols,
                        cursorRelativeLeafIndex: nodeState.Node.LeafCount - nodeState.CursorLeafIndex,
                        expandedSecond: nodeState == state.Second
                    );
                    if (state.Context.CanBeFollowedWith(context))
                    {
                        foreach (var reference in this._nonDiscriminatorSymbolReferences[nodeState.Node.Symbol])
                        {
                            yield return (nodeState.ExpandRoot(reference.rule, reference.index), context);
                        }
                    }
                }
                else if (nodeState.TrailingUnmatchedSymbols.Head is NonTerminal nextTrailingNonTerminal)
                {
                    var leafIndexOfNextTrailing = nodeState.Node.LeafCount - nodeState.TrailingUnmatchedSymbols.Count;
                    var context = new SearchContext(
                        SearchAction.RemoveUnmatchedTrailingSymbols, 
                        cursorRelativeLeafIndex: leafIndexOfNextTrailing - nodeState.CursorLeafIndex, 
                        expandedSecond: nodeState == state.Second
                    );
                    if (state.Context.CanBeFollowedWith(context))
                    {
                        foreach (var rule in this._rulesByProduced[nextTrailingNonTerminal])
                        {
                            yield return (nodeState.ExpandLeaf(leafIndexOfNextTrailing, rule), context);
                        }
                    }
                }
            }
        }

        private IEnumerable<SearchState> ExpandLeafToResolveUnmatchedLeadingSymbols(SearchState state)
        {
            Invariant.Require(!state.HasUnmatchedTrailingSymbols);

            var firstExpansions = ExpandLeafToResolveUnmatchedLeadingSymbols(state.First);
            var secondExpansions = ExpandLeafToResolveUnmatchedLeadingSymbols(state.Second);

            return firstExpansions.Concat(secondExpansions)
                .Select(t => ToSearchState(state, t.expanded, t.context));

            IEnumerable<(NodeState expanded, SearchContext context)> ExpandLeafToResolveUnmatchedLeadingSymbols(NodeState nodeState)
            {
                if (nodeState.LeadingUnmatchedSymbols.Count == 0)
                {
                    var context = new SearchContext(
                        SearchAction.RemoveUnmatchedLeadingSymbols,
                        cursorRelativeLeafIndex: -1 - nodeState.CursorLeafIndex,
                        expandedSecond: nodeState == state.Second
                    );
                    if (state.Context.CanBeFollowedWith(context))
                    {
                        foreach (var reference in this._nonDiscriminatorSymbolReferences[nodeState.Node.Symbol])
                        {
                            yield return (nodeState.ExpandRoot(reference.rule, reference.index), context);
                        }
                    }
                }
                else if (nodeState.LeadingUnmatchedSymbols.Head is NonTerminal nextLeadingNonTerminal)
                {
                    var leafIndexOfNextLeading = nodeState.LeadingUnmatchedSymbols.Count - 1;
                    var context = new SearchContext(
                        SearchAction.RemoveUnmatchedLeadingSymbols,
                        cursorRelativeLeafIndex: leafIndexOfNextLeading - nodeState.CursorLeafIndex,
                        expandedSecond: nodeState == state.Second
                    );
                    if (state.Context.CanBeFollowedWith(context))
                    {
                        foreach (var rule in this._rulesByProduced[nextLeadingNonTerminal])
                        {
                            yield return (nodeState.ExpandLeaf(leafIndexOfNextLeading, rule), context);
                        }
                    }
                }
            }
        }

        private IEnumerable<SearchState> ExpandRootToResolveMismatchedRoots(SearchState state)
        {
            Invariant.Require(!state.HasUnmatchedLeadingSymbols);

            var firstExpansions = ExpandRootToResolveMismatchedRoots(state.First);
            var secondExpansions = ExpandRootToResolveMismatchedRoots(state.Second);

            return firstExpansions.Concat(secondExpansions)
                .Select(t => ToSearchState(state, t.expanded, t.context));

            IEnumerable<(NodeState expanded, SearchContext context)> ExpandRootToResolveMismatchedRoots(NodeState nodeState)
            {
                var context = new SearchContext(SearchAction.RemoveMismatchedRoots, cursorRelativeLeafIndex: 0, expandedSecond: nodeState == state.Second);
                if (state.Context.CanBeFollowedWith(context))
                {
                    foreach (var reference in this._nonDiscriminatorSymbolReferences[nodeState.Node.Symbol])
                    {
                        yield return (nodeState.ExpandRoot(reference.rule, reference.index), context);
                    }
                }
            }
        }

        private static SearchState ToSearchState(SearchState original, NodeState expanded, SearchContext context)
        {
            return new SearchState(
                first: context.ExpandedSecond ? original.First : expanded,
                second: context.ExpandedSecond ? expanded : original.Second,
                context: context
            );
        }

        private static int GetCursorLeafIndex(PotentialParseNode node)
        {
            var cursorPosition = node.CursorPosition.Value;
            
            if (node is PotentialParseLeafNode leaf)
            {
                return cursorPosition == 0 ? 0 : 1;
            }
            else if (node is PotentialParseParentNode parent)
            {
                if (cursorPosition == parent.Children.Count) { return node.LeafCount; } // trailing cursor

                // sum leaves before the cursor child plus any leaves within the cursor child that are before the cursor
                var result = GetCursorLeafIndex(parent.Children[cursorPosition]);
                for (var i = 0; i < cursorPosition; ++i)
                {
                    result += parent.Children[i].LeafCount;
                }
                return result;
            }
            else
            {
                throw new ArgumentException("Unexpected node type");
            }
        }

        private static PotentialParseLeafNode GetLeaf(PotentialParseNode node, int index)
        {
            Invariant.Require(index >= 0 && index <= node.LeafCount);

            return index == node.LeafCount ? null : GetLeafHelper(node, index);

            PotentialParseLeafNode GetLeafHelper(PotentialParseNode currentNode, int currentIndex)
            {
                if (node is PotentialParseLeafNode leaf) { return leaf; }
                if (node is PotentialParseParentNode parent)
                {
                    var adjustedIndex = currentIndex;
                    for (var i = 0; i < parent.Children.Count; ++i)
                    {
                        var child = parent.Children[i];
                        if (child.LeafCount > adjustedIndex)
                        {
                            return GetLeafHelper(child, adjustedIndex);
                        }
                        else
                        {
                            adjustedIndex -= child.LeafCount;
                        }
                    }
                }

                throw new InvalidOperationException("should never get here");
            }
        }

        private sealed class NodeState
        {
            public NodeState(PotentialParseParentNode node)
            {
                this.Node = node;
                this.CursorLeafIndex = GetCursorLeafIndex(node);
                this.ExpansionCount = 0;
                this.TrailingUnmatchedSymbols = node.Leaves.Skip(this.CursorLeafIndex).Select(l => l.Symbol).ToImmutableLinkedList();
                this.LeadingUnmatchedSymbols = node.Leaves.Take(this.CursorLeafIndex).Select(l => l.Symbol).Reverse().ToImmutableLinkedList();
            }

            private NodeState(
                PotentialParseParentNode node, 
                int cursorLeafIndex,
                int expansionCount,
                ImmutableLinkedList<Symbol> trailingUnmatchedSymbols,
                ImmutableLinkedList<Symbol> leadingUnmatchedSymbols)
            {
                this.Node = node;
                this.CursorLeafIndex = cursorLeafIndex;
                this.ExpansionCount = expansionCount;
                this.TrailingUnmatchedSymbols = trailingUnmatchedSymbols;
                this.LeadingUnmatchedSymbols = leadingUnmatchedSymbols;
            }

            public PotentialParseParentNode Node { get; }
            public int CursorLeafIndex { get; }
            public int ExpansionCount { get; }
            public bool HasTrailingCursor => this.CursorLeafIndex == this.Node.LeafCount;
            public int LeafCountIncludingTrailingCursor => this.Node.LeafCount + (this.HasTrailingCursor ? 1 : 0);
            public ImmutableLinkedList<Symbol> TrailingUnmatchedSymbols { get; }
            public ImmutableLinkedList<Symbol> LeadingUnmatchedSymbols { get; }

            public static (NodeState a, NodeState b) Align(NodeState a, NodeState b)
            {
                // see if there are any trailing matches
                var (aTrailing, bTrailing) = StripCommonPrefix(a.TrailingUnmatchedSymbols, b.TrailingUnmatchedSymbols);
                var (aLeading, bLeading) = StripCommonPrefix(a.LeadingUnmatchedSymbols, b.LeadingUnmatchedSymbols);

                // to check for change, we only need to consider a since either both change or neither change
                if (aTrailing.Count == a.TrailingUnmatchedSymbols.Count
                    && aLeading.Count == b.LeadingUnmatchedSymbols.Count)
                {
                    return (a, b); // no change
                }

                return (
                    new NodeState(a.Node, a.CursorLeafIndex, a.ExpansionCount, trailingUnmatchedSymbols: aTrailing, leadingUnmatchedSymbols: aLeading),
                    new NodeState(b.Node, b.CursorLeafIndex, b.ExpansionCount, trailingUnmatchedSymbols: bTrailing, leadingUnmatchedSymbols: bLeading)
                );
            }

            public NodeState ExpandLeaf(int leafIndex, Rule rule)
            {
                var newNode = ReplaceLeafInNode(this.Node, leafIndex);

                ImmutableLinkedList<Symbol> newTrailing = this.TrailingUnmatchedSymbols, newLeading = this.LeadingUnmatchedSymbols;
                if (leafIndex >= this.CursorLeafIndex)
                {
                    Invariant.Require(newTrailing.Head == rule.Produced);

                    newTrailing = newTrailing.Tail; // pop the symbol to be replaced
                    for (var i = rule.Symbols.Count - 1; i >= 0; --i)
                    {
                        newTrailing = newTrailing.Prepend(rule.Symbols[i]);
                    }
                }
                else
                {
                    Invariant.Require(newLeading.Head == rule.Produced);

                    newLeading = newLeading.Tail; // pop the symbol to be replaced
                    for (var i = 0; i < rule.Symbols.Count; ++i)
                    {
                        newTrailing = newTrailing.Prepend(rule.Symbols[i]);
                    }
                }
                
                return new NodeState(
                    newNode,
                    // it may seem like this should also move in the case where we replace the cursor leaf
                    // with a null production. However, in that case the cursor actually stays in the same
                    // absolute leaf index since the cursor is always on the first trailing leaf (or is entirely trailing)
                    this.CursorLeafIndex + (newLeading.Count - this.LeadingUnmatchedSymbols.Count),
                    this.ExpansionCount + 1,
                    trailingUnmatchedSymbols: newTrailing, 
                    leadingUnmatchedSymbols: newLeading
                );
                
                PotentialParseParentNode ReplaceLeafInNode(PotentialParseNode node, int leafIndexInNode)
                {
                    if (node is PotentialParseParentNode parent)
                    {
                        var adjustedLeafIndex = leafIndexInNode;
                        for (var i = 0; i < parent.Children.Count; ++i)
                        {
                            var child = parent.Children[i];
                            if (child.LeafCount > adjustedLeafIndex)
                            {
                                // rebuild child
                                var childWithLeafReplaced = ReplaceLeafInNode(child, adjustedLeafIndex);

                                // we need to handle the edge case when the original child had the cursor and
                                // the new child does not (which happens when replacing with a null production)
                                if (child.CursorPosition.HasValue && !childWithLeafReplaced.CursorPosition.HasValue)
                                {
                                    // if we have any following children, tack the cursor on to the first one with leaves
                                    // or as trailing to the final one. If we have NO following children, the cursor gets re-added
                                    // as trailing to the replaced child
                                    if (i < parent.Children.Count - 1)
                                    {
                                        var indexToAddCursor = i + 1;
                                        do
                                        {
                                            if (parent.Children[indexToAddCursor].LeafCount > 0) { break; }
                                            ++indexToAddCursor;
                                        }
                                        while (indexToAddCursor < parent.Children.Count);

                                        return new PotentialParseParentNode(
                                            parent.Rule,
                                            parent.Children.Select((ch, index) => index == i ? childWithLeafReplaced : index == indexToAddCursor ? parent.Children[index].WithCursor(0) : parent.Children[index])
                                        );
                                    }
                                    childWithLeafReplaced = childWithLeafReplaced.WithCursor(0); // will be trailing
                                }
                                return new PotentialParseParentNode(parent.Rule, parent.Children.Select((ch, index) => index == i ? childWithLeafReplaced : parent.Children[index]));
                            }

                            adjustedLeafIndex -= child.LeafCount;
                        }
                    }

                    Invariant.Require(leafIndexInNode == 0 && node.Symbol == rule.Produced);

                    if (node.CursorPosition.HasValue) // we're replacing the leaf with the cursor
                    {
                        // place the cursor on the first new leaf we create (note that if the rule has no symbols this won't place a cursor; our caller will take care of that)
                        return new PotentialParseParentNode(rule, rule.Symbols.Select((s, i) => new PotentialParseLeafNode(s, cursorPosition: i == 0 ? 0 : default(int?))));
                    }

                    return new PotentialParseParentNode(rule, rule.Symbols.Select(s => new PotentialParseLeafNode(s)));
                }
            }

            public NodeState ExpandRoot(Rule rule, int symbolIndex)
            {
                Invariant.Require(this.Node.Symbol == rule.Symbols[symbolIndex]);

                // if we have a trailing cursor and rule will add new trailing symbols, we'll need to move the cursor
                // to the first of those new trailing symbols
                var willMoveCursor = this.HasTrailingCursor && symbolIndex < rule.Symbols.Count - 1;

                return new NodeState(
                    new PotentialParseParentNode(
                        rule,
                        rule.Symbols.Select(
                            (s, i) =>
                            {
                                if (i == symbolIndex)
                                {
                                    return willMoveCursor ? this.Node.WithoutCursor() : this.Node;
                                }
                                if (willMoveCursor && i == symbolIndex + 1)
                                {
                                    return new PotentialParseLeafNode(s, cursorPosition: 0);
                                }
                                return new PotentialParseLeafNode(s);
                            }
                        )
                    ),
                    this.CursorLeafIndex + symbolIndex,
                    this.ExpansionCount + 1,
                    trailingUnmatchedSymbols: this.TrailingUnmatchedSymbols.AppendRange(rule.Skip(symbolIndex + 1).Symbols),
                    leadingUnmatchedSymbols: this.LeadingUnmatchedSymbols.AppendRange(rule.Symbols.Take(symbolIndex).Reverse())
                );
            }

            private static (ImmutableLinkedList<Symbol> a, ImmutableLinkedList<Symbol> b) StripCommonPrefix(
                ImmutableLinkedList<Symbol> a, 
                ImmutableLinkedList<Symbol> b)
            {
                var changedA = a;
                var changedB = b;
                while (changedA.TryDeconstruct(out var aHead, out var aTail)
                    && changedB.TryDeconstruct(out var bHead, out var bTail)
                    && aHead == bHead)
                {
                    changedA = aTail;
                    changedB = bTail;
                }

                return (changedA, changedB);
            }
        }

        private sealed class SearchState : IComparable<SearchState>
        {
            public SearchState(
                NodeState first,
                NodeState second,
                SearchContext context)
            {
                (this.First, this.Second) = NodeState.Align(first, second);
                this.Context = context;
            }

            public NodeState First { get; }
            public NodeState Second { get; }
            public SearchContext Context { get; }

            public int TotalExpansionCount => this.First.ExpansionCount + this.Second.ExpansionCount;
            public bool HasAnyTrailingCursor => this.First.HasTrailingCursor || this.Second.HasTrailingCursor;
            public bool HasUnmatchedTrailingSymbols => this.First.TrailingUnmatchedSymbols.Count > 0 || this.Second.TrailingUnmatchedSymbols.Count > 0;
            public bool HasUnmatchedLeadingSymbols => this.First.LeadingUnmatchedSymbols.Count > 0 || this.Second.LeadingUnmatchedSymbols.Count > 0;
            public bool HasMismatchedRoots => this.First.Node.Symbol != this.Second.Node.Symbol;

            public int MaxLeafCountIncludingTrailingCursor => Math.Max(this.First.LeafCountIncludingTrailingCursor, this.Second.LeafCountIncludingTrailingCursor);

            private double FractionLeavesMatched
            {
                get
                {
                    // we use MAX for the denominator since eventually all leaves will need to be matched up (either by eliminating the
                    // leaf through an empty production or by matching the leaf through an expansion). Therefore, I think MAX does a better
                    // job than SUM of accounting for the work remaining to be done (either one is arguably "correct")
                    var totalLeafCount = 2 * Math.Max(this.First.LeafCountIncludingTrailingCursor, this.Second.LeafCountIncludingTrailingCursor);
                    var matchedLeafCount = CountMatchedLeaves(this.First) + CountMatchedLeaves(this.Second);
                    return matchedLeafCount / (double)totalLeafCount;

                    int CountMatchedLeaves(NodeState nodeState) => nodeState.LeafCountIncludingTrailingCursor
                        - nodeState.LeadingUnmatchedSymbols.Count
                        - nodeState.TrailingUnmatchedSymbols.Count
                        - (nodeState.HasTrailingCursor ? 1 : 0);
                }
            }

            public int CompareTo(SearchState that)
            {
                // if one state has matched a higher % of leaves, then that state is better (LESS)
                var matchFractionComparison = that.FractionLeavesMatched.CompareTo(this.FractionLeavesMatched);
                if (matchFractionComparison != 0)
                {
                    return matchFractionComparison;
                }

                // if one state has a lower max # of leaves, then that state is better (LESS)
                var maxLeafCountComparison = this.MaxLeafCountIncludingTrailingCursor.CompareTo(that.MaxLeafCountIncludingTrailingCursor);
                if (maxLeafCountComparison != 0)
                {
                    return maxLeafCountComparison;
                }

                // if one state has matched roots, then that state is better (LESS)
                var matchingRootCountComparison = this.HasMismatchedRoots.CompareTo(that.HasMismatchedRoots);
                if (matchFractionComparison != 0)
                {
                    return matchingRootCountComparison;
                }

                // if one state is less complex (fewer total nodes), then that state is better (LESS)
                int CountNodes(PotentialParseNode node)
                {
                    return node is PotentialParseParentNode parent ? 1 + parent.Children.Sum(CountNodes)
                        : node is PotentialParseLeafNode leaf ? 1
                        : throw new ArgumentException("Unexpected node type");
                }
                var nodeCountComparison = (CountNodes(this.First.Node) + CountNodes(this.Second.Node)).CompareTo(CountNodes(that.First.Node) + CountNodes(that.Second.Node));
                if (nodeCountComparison != 0)
                {
                    return nodeCountComparison;
                }

                // we should basically never get here, but fall back to an alphabetical comparison
                // just to guarantee a total order
                string ToString(SearchState state) => state.First.Node.ToMarkedString() + Environment.NewLine + state.Second.Node.ToMarkedString();
                return ToString(this).CompareTo(ToString(that));
            }
        }

        private struct SearchContext
        {
            public SearchContext(SearchAction action, int cursorRelativeLeafIndex, bool expandedSecond)
            {
                this.Action = action;
                this.CursorRelativeLeafIndex = cursorRelativeLeafIndex;
                this.ExpandedSecond = expandedSecond;
            }

            public SearchAction Action { get; }
            public int CursorRelativeLeafIndex { get; }
            public bool ExpandedSecond { get; }

            /// <summary>
            /// Determines whether the search can proceed from this context to <paramref name="that"/> one.
            /// 
            /// The compatibility rules exist to avoid duplicate work
            /// </summary>
            public bool CanBeFollowedWith(SearchContext that)
            {
                return this.Action != that.Action
                    || this.CursorRelativeLeafIndex != that.CursorRelativeLeafIndex
                    || !this.ExpandedSecond
                    || that.ExpandedSecond;
            }
        }

        private enum SearchAction
        {
            Initial,
            RemoveTrailingCursor,
            RemoveUnmatchedTrailingSymbols,
            RemoveUnmatchedLeadingSymbols,
            RemoveMismatchedRoots,
        }
    }
}
