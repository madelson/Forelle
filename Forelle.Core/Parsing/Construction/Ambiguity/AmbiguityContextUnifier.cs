using Medallion.Collections;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;

namespace Forelle.Parsing.Construction.Ambiguity
{
    /// <summary>
    /// This class is responsible for finding a "unified" ambiguity given two <see cref="PotentialParseParentNode" />s that represent an ambiguity 
    /// point. A "unified" ambiguity is a true example ambiguity in the grammar: two different parse trees for the same sequence of symbols encountered
    /// upon parsing the same symbol in the grammar.
    /// 
    /// NOTE that this attempts to find ONE representative ambiguity; it cannot find ALL possible ambiguous trees (there could be an infinite number).
    /// 
    /// The methodology is based on A* search; starting with each node, we will consider various grammatical expansions until both nodes have the same
    /// set of leaves and the same root <see cref="NonTerminal"/>. We align leaves based on the <see cref="PotentialParseNode.CursorPosition" /> to stay
    /// true to how the parser would encounter the ambiguity.
    /// </summary>
    internal class AmbiguityContextUnifier
    {
        /// <summary>
        /// Used to keep the algorithm from hanging forever, especially in cases where there are an unbounded number of expansions that we could try
        /// </summary>
        private const int MaxExpansionCount = 20;

        private readonly IReadOnlyDictionary<NonTerminal, IReadOnlyList<Rule>> _rulesByProduced;
        private readonly FirstFollowLastPrecedingCalculator _firstFollowLastPreceding;
        /// <summary>
        /// Cached lookup for where various <see cref="Symbol" />s are referenced by other
        /// <see cref="Rule"/>s
        /// </summary>
        private readonly ILookup<Symbol, (Rule rule, int index)> _nonDiscriminatorSymbolReferences;

        public AmbiguityContextUnifier(IReadOnlyDictionary<NonTerminal, IReadOnlyList<Rule>> rulesByProduced)
        {
            this._rulesByProduced = rulesByProduced;

            var nonDiscriminatorRules = this._rulesByProduced.Where(kvp => !(kvp.Key.SyntheticInfo is DiscriminatorSymbolInfo))
                .SelectMany(kvp => kvp.Value)
                .ToArray();

            this._firstFollowLastPreceding = FirstFollowLastPrecedingCalculator.Create(nonDiscriminatorRules);
            
            this._nonDiscriminatorSymbolReferences = nonDiscriminatorRules
                .SelectMany(r => r.Symbols.Select((s, i) => (referenced: s, index: i, rule: r)))
                .ToLookup(t => t.referenced, t => (rule: t.rule, index: t.index));
        }

        /// <summary>
        /// Attempts to unify <paramref name="firsts"/> and <paramref name="seconds"/>; a unification of any pair (first, second) will be
        /// considered a success. <paramref name="lookahead"/> is the next token in view for the parser.
        /// </summary>
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

        /// <summary>
        /// Creates an initial set of <see cref="SearchState"/>s by cross-joining the given <see cref="PotentialParseParentNode" />s
        /// </summary>
        private static IEnumerable<SearchState> GetInitialSearchStates(
            IReadOnlyCollection<PotentialParseParentNode> firsts, 
            IReadOnlyCollection<PotentialParseParentNode> seconds)
        {
            var secondStates = seconds.Select(NodeState.CreateInitial).ToArray();

            return from firstState in firsts.Select(NodeState.CreateInitial)
                   from secondState in secondStates
                   select new SearchState(firstState, secondState, new SearchContext(SearchAction.Initial, cursorRelativeLeafIndex: 0, expandedSecond: false));
        }

        /// <summary>
        /// Given a <paramref name="state"/> that is not a dead end and not a success, <paramref name="lookahead"/>
        /// generates a list of subsequent <see cref="SearchState"/>s to try.
        /// </summary>
        private IEnumerable<SearchState> GetNextSearchStates(SearchState state, Token lookahead)
        {
            // rather than considering all ways to transform the given state, we have a prioritized list of
            // transformation types. The purpose of this is to avoid duplicate work (we don't want to consider both
            // transformation A, then B as well as B, then A).
            
            // first, look to resolve any trailing cursor. Note that once trailing cursors are resolved, we
            // can't get back into having a trailing cursor through further transformations
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

        /// <summary>
        /// Determines whether the search cannot progress from <paramref name="state"/>
        /// </summary>
        private bool IsDeadEnd(SearchState state, Token lookahead)
        {
            // checking total expansion count is a heuristic that allows the search to terminate
            // eventually when their is no real ambiguity and only a limitation of our algorithm (the palindrome
            // grammar is a good example). Of course, the downside is that we might not discover some real ambiguities.
            // The hope is that this number is large enough to handle practical cases
            return state.TotalExpansionCount == MaxExpansionCount
                || IsDeadEndBasedOnTokenMatching(state.First, state.Second)
                || IsDeadEndBasedOnTokenMatching(state.Second, state.First);
            
            // This check looks to eliminate search states based on grammatical rules. For example, if the next symbol to match
            // up in a is a "+" token but "+" can't appear in the remainder of b, then no amount of expansion will help
            bool IsDeadEndBasedOnTokenMatching(NodeState a, NodeState b)
            {
                // trailing check
                // see if a's next symbol to match is a token
                var requiredTrailingTokenFromA = a.TrailingUnmatchedSymbols.TryDeconstruct(out var nextTrailing, out _) && nextTrailing is Token nextTrailingToken 
                    ? nextTrailingToken
                    : null;
                Token requiredTrailingToken;
                // see if the next trailing symbol to match must start with the lookahead token. This will also be true if we have a trailing cursor
                var requireLookaheadToken = (b.Node.LeafCount - b.TrailingUnmatchedSymbols.Count) <= b.CursorLeafIndex;
                if (requireLookaheadToken)
                {
                    // if we need to match the lookahead and token from a, then those must match
                    if (requiredTrailingTokenFromA != null && requiredTrailingTokenFromA != lookahead) { return true; }
                    requiredTrailingToken = lookahead;
                }
                else { requiredTrailingToken = requiredTrailingTokenFromA; }
                
                // if we have a required match token, make sure that it can appear
                if (requiredTrailingToken != null)
                {
                    var canHaveTokenInTrailing = CanHaveToken(b.TrailingUnmatchedSymbols, requiredTrailingToken, (p, s) => p.FirstOf(s));
                    if (!(canHaveTokenInTrailing ?? this._firstFollowLastPreceding.FollowOf(b.Node.Rule.Produced).Contains(requiredTrailingToken)))
                    {
                        return true;
                    }
                }

                // leading check (simpler version of trailing check that doesn't need to consider the lookahead)
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

        /// <summary>
        /// Considers root expansions of <paramref name="state"/> with the goal of eliminating any trailing cursors
        /// from the <see cref="NodeState"/>s
        /// </summary>
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
                    // note: this NextOf check is technically redundant due to IsDeadEnd, but having it here is a cheap 
                    // way to avoid even creating some extra states
                    .Where(reference => this._firstFollowLastPreceding.NextOfContains(reference.rule.Skip(reference.index + 1), lookahead));
                return usableReferences.Select(reference => nodeState.ExpandRoot(reference.rule, reference.index));
            }
        }

        /// <summary>
        /// Considers expansions of <paramref name="state"/>, with the ultimate goal of eliminating all <see cref="NodeState.TrailingUnmatchedSymbols"/>
        /// (note that sometimes we may have to increase the number of unmatched symbols before we can decrease it, though). These can either be root expansions
        /// or unmatched symbol expansions
        /// </summary>
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
                    // if we have no unmatched trailing symbols, consider root expansions that might help us match 
                    // the other node's trailing symbols

                    var context = new SearchContext(
                        SearchAction.RemoveUnmatchedTrailingSymbols,
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

        /// <summary>
        /// Considers expansions of <paramref name="state"/>, with the ultimate goal of eliminating all <see cref="NodeState.LeadingUnmatchedSymbols"/>
        /// (note that sometimes we may have to increase the number of unmatched symbols before we can decrease it, though). These can either be root expansions
        /// or unmatched symbol expansions
        /// </summary>
        private IEnumerable<SearchState> ExpandLeafToResolveUnmatchedLeadingSymbols(SearchState state)
        {
            Invariant.Require(!state.HasUnmatchedTrailingSymbols);

            var firstExpansions = ExpandLeafToResolveUnmatchedLeadingSymbols(state.First);
            var secondExpansions = ExpandLeafToResolveUnmatchedLeadingSymbols(state.Second);

            return firstExpansions.Concat(secondExpansions)
                .Select(t => ToSearchState(state, t.expanded, t.context));

            IEnumerable<(NodeState expanded, SearchContext context)> ExpandLeafToResolveUnmatchedLeadingSymbols(NodeState nodeState)
            {
                // if we have no unmatched leading symbols, consider root expansions that might help us match 
                // the other node's trailing symbols

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

        /// <summary>
        /// Considers root expansions of <see cref="state"/> with the goal of getting all node
        /// roots to match
        /// </summary>
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
            
            if (node is PotentialParseParentNode parent)
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

            return cursorPosition == 0 ? 0 : 1;
        }
        
        private sealed class NodeState
        {
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
            /// <summary>
            /// The number of expansions (calls to <see cref="ExpandLeaf(int, Rule)"/> and <see cref="ExpandRoot(Rule, int)"/>)
            /// that have been called to derive this <see cref="NodeState"/>
            /// </summary>
            public int ExpansionCount { get; }
            public bool HasTrailingCursor => this.CursorLeafIndex == this.Node.LeafCount;
            public int LeafCountIncludingTrailingCursor => this.Node.LeafCount + (this.HasTrailingCursor ? 1 : 0);
            /// <summary>
            /// Contains all leaf <see cref="Symbol"/>s at or after the cursor position that have yet to be matched
            /// up with the other <see cref="NodeState"/>
            /// </summary>
            public ImmutableLinkedList<Symbol> TrailingUnmatchedSymbols { get; }
            /// <summary>
            /// Contains all leaf <see cref="Symbol"/>s before the cursor position that have yet to be matched
            /// up with the other <see cref="NodeState"/>.
            /// 
            /// The order of this collection is reversed, such that the first element is the unmatched symbol closest
            /// to the cursor. This is done to make it easy to find the next match and to make trimming matches cheap
            /// </summary>
            public ImmutableLinkedList<Symbol> LeadingUnmatchedSymbols { get; }

            public static NodeState CreateInitial(PotentialParseParentNode node)
            {
                var cursorLeafIndex = GetCursorLeafIndex(node);
                return new NodeState(
                    node,
                    cursorLeafIndex,
                    expansionCount: 0,
                    // note: this could be done more efficiently, but it's not worth it because CreateInitial isn't called often
                    trailingUnmatchedSymbols: node.GetLeaves().Skip(cursorLeafIndex).Select(l => l.Symbol).ToImmutableLinkedList(),
                    leadingUnmatchedSymbols: node.GetLeaves().Take(cursorLeafIndex).Select(l => l.Symbol).Reverse().ToImmutableLinkedList()
                );
            }

            /// <summary>
            /// Given two <see cref="NodeState"/>s, returns them with their <see cref="TrailingUnmatchedSymbols"/>
            /// and <see cref="LeadingUnmatchedSymbols"/> adjusted to reflect matches
            /// </summary>
            public static (NodeState a, NodeState b) Align(NodeState a, NodeState b)
            {
                // see if there are any trailing matches. Note that if the cursor is on a non-terminal this this can never
                // match: we want to force the cursor to be placed on a token through expansions to ensure that the placement
                // of the cursor in a unified answer is always precise
                var (aTrailing, bTrailing) = HasCursorOnNonTerminalLeaf(a) || HasCursorOnNonTerminalLeaf(b)
                    ? (a.TrailingUnmatchedSymbols, b.TrailingUnmatchedSymbols)
                    : StripCommonPrefix(a.TrailingUnmatchedSymbols, b.TrailingUnmatchedSymbols);
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

                // this relies on the fact that we never move a non-terminal cursor out of TrailingUnmatchedSymbols
                bool HasCursorOnNonTerminalLeaf(NodeState state) => state.CursorLeafIndex == (state.Node.LeafCount - state.TrailingUnmatchedSymbols.Count)
                    && state.TrailingUnmatchedSymbols.TryDeconstruct(out var head, out _)
                    && head is NonTerminal;
            }

            /// <summary>
            /// Expands the <paramref name="leafIndex"/>th leaf node using <paramref name="rule"/>
            /// </summary>
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

            /// <summary>
            /// Expands by wrapping with a new <see cref="PotentialParseParentNode"/> derived from <paramref name="rule"/>.
            /// The current <see cref="Node"/> will be placed at <paramref name="symbolIndex"/>
            /// </summary>
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

        /// <summary>
        /// Represents a state in the A* search. This class is <see cref="IComparable{T}"/> to allow it to
        /// be used with a <see cref="PriorityQueue{T}"/>
        /// </summary>
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
                // this comparison method is a HEURISTIC whose goal is to get us to an answer as quickly as possible. It does
                // not guarantee that we arrive at the simplest answer, although in practice this seems to happen reliably

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

        /// <summary>
        /// <see cref="SearchContext"/> is part of what helps us avoid duplicate work by capturing the context in which
        /// a <see cref="SearchState"/> was created. The idea is that we want to avoid reaching the same
        /// state via two paths where one expands <see cref="SearchState.First"/> first and one expands <see cref="SearchState.Second"/> first.
        /// 
        /// The other piece that helps avoid this is the order in which we consider expansion types in <see cref="GetNextSearchStates(SearchState, Token)"/>
        /// </summary>
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
