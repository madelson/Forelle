using Medallion.Collections;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;

namespace Forelle.Parsing.Construction.New2
{
    internal class ParserGenerator
    {
        private readonly ILookup<NonTerminal, Rule> _rulesByProduced;
        private readonly IFirstFollowProvider _firstFollow;
        private readonly IReadOnlyDictionary<Rule, PotentialParseParentNode> _defaultParses;
        private readonly IReadOnlyDictionary<NonTerminal, ParsingContext> _defaultParsingContexts;

        private readonly Stack<ParserGeneratorState> _stateStack = new Stack<ParserGeneratorState>(); 

        private ParserGenerator(IReadOnlyList<Rule> rules)
        {
            this._rulesByProduced = rules.ToLookup(r => r.Produced);
            this._firstFollow = FirstFollowCalculator.Create(rules);

            this._defaultParses = rules.ToDictionary(r => r, r => DefaultParseOf(r).WithCursor(0));
            this._defaultParsingContexts = this._rulesByProduced.ToDictionary(
                g => g.Key,
                g => this.CreateContext(g.Select(r => this._defaultParses[r]))
            );

            this._stateStack.Push(ParserGeneratorState.Empty);
        }

        public static (IReadOnlyDictionary<StartSymbolInfo, ParsingContext> startContexts, IReadOnlyDictionary<ParsingContext, ParsingAction> contextActions) Generate(IReadOnlyList<Rule> rules)
        {
            var generator = new ParserGenerator(rules);
            return generator.Solve();
        }

        private (IReadOnlyDictionary<StartSymbolInfo, ParsingContext> startContexts, IReadOnlyDictionary<ParsingContext, ParsingAction> contextActions) Solve()
        {
            var startSymbolContexts = this._defaultParsingContexts
                .Select(kvp => (startInfo: kvp.Key.SyntheticInfo as StartSymbolInfo, context: kvp.Value))
                .Where(t => t.startInfo != null)
                .ToDictionary(t => t.startInfo, t => t.context);
            
            foreach (var startSymbolContext in startSymbolContexts.Values)
            {
                var result = this.TrySolve(startSymbolContext);
                if (!result.IsSuccessful)
                {
                    throw new NotImplementedException();
                }
                Invariant.Require(this._stateStack.Count == 1 && !this._stateStack.Peek().HasPendingContexts);
            }

            return (startSymbolContexts, this._stateStack.Single().SolvedContexts);
        }

        private ParserGenerationResult TrySolve(ParsingContext context)
        {
            var currentState = this._stateStack.Peek();
            if (currentState.SolvedContexts.ContainsKey(context)) { return ParserGenerationResult.Success; }

            var newState = currentState.AddToSolve(context);
            if (newState == currentState) { return ParserGenerationResult.Success; }

            this._stateStack.Push(newState);
            var actionOrError = this.TrySolveHelper(context);
            var resultState = this._stateStack.Pop();

            if (actionOrError.Action != null)
            {
                this._stateStack.Pop(); // pop currentState
                this._stateStack.Push(resultState.AddSolution(context, actionOrError.Action));
                return ParserGenerationResult.Success;
            }

            return new ParserGenerationResult(actionOrError.ErrorContext);
        }

        private ParsingActionOrError TrySolveHelper(ParsingContext context)
        {
            var nodes = context.Nodes;

            // simplest case: we have one node with a trailing cursor: just reduce
            if (nodes.Count == 1 && nodes.Single().HasTrailingCursor())
            {
                return new ReduceAction(nodes);
            }

            // next, see if we can narrow down the node set using LL(1) lookahead
            if (nodes.Count > 1)
            {
                var nodesToNextSets = nodes.ToDictionary(n => n, n => this.GetNextSetFromCursor(n).Intersect(context.LookaheadTokens));
                if (nodesToNextSets.Values.Distinct(ImmutableHashSetComparer<Token>.Instance).Count() > 1)
                {
                    var subContexts = nodesToNextSets.Values.Aggregate((s1, s2) => s1.Union(s2))
                        .GroupBy(
                            t => nodesToNextSets.Where(kvp => kvp.Value.Contains(t)).Select(kvp => kvp.Key).ToArray(), 
                            EqualityComparers.GetSequenceComparer<PotentialParseParentNode>()
                        )
                        .Select(g => new ParsingContext(g.Key, lookaheadTokens: g));
                    foreach (var subContext in subContexts)
                    {
                        var subResult = this.TrySolve(subContext);
                        if (!subResult.IsSuccessful) { return subResult.ErrorContext; }
                    }

                    return new TokenSwitchAction(subContexts.SelectMany(c => c.LookaheadTokens, (c, t) => (context: c, token: t)).ToDictionary(t => t.token, t => t.context));
                }
            }

            // if everything has a trailing cursor, then we're at a reduce-reduce conflict
            if (nodes.All(n => n.HasTrailingCursor()))
            {
                return new ReduceAction(nodes);
            }

            // if just some of the nodes have a traililng cursor, then we're at a shift-reduce conflict
            if (nodes.Any(n => n.HasTrailingCursor()))
            {
                return context;
            }
            
            // next, see if we're at a common prefix
            var nodesToNextLeaves = nodes.ToDictionary(n => n, n => n.GetLeafAtCursorPosition());
            if (nodesToNextLeaves.Values.Select(n => n.Symbol).Distinct().Count() == 1)
            {
                var singleSymbol = nodesToNextLeaves.Values.First().Symbol;
                var nextContext = this.CreateContext(nodes.Select(n => AdvanceCursor(n, 1)));
                if (singleSymbol is Token token)
                {
                    var nextContextResult = this.TrySolve(nextContext);
                    if (!nextContextResult.IsSuccessful) { return nextContextResult.ErrorContext; }
                    return new EatTokenAction(token, next: nextContext);
                }
                else
                {
                    var nonTerminal = (NonTerminal)singleSymbol;
                    var nonTerminalContext = this._defaultParsingContexts[nonTerminal];
                    var nonTerminalContextResult = this.TrySolve(nonTerminalContext);
                    if (nonTerminalContextResult.IsSuccessful)
                    {
                        var nextContextResult = this.TrySolve(nextContext);
                        if (!nextContextResult.IsSuccessful) { return nextContextResult.ErrorContext; }
                        return new ParseContextAction(nonTerminalContext, next: nextContext);
                    }

                    // try lifting
                    var liftedNodes = new List<PotentialParseParentNode>();
                    foreach (var node in nodes)
                    {
                        if (this.TryLiftSymbolAtCursor(node, out var lifted))
                        {
                            liftedNodes.AddRange(lifted);
                        }
                        else
                        {
                            return context;
                        }
                    }
                    var liftedContext = new ParsingContext(liftedNodes, context.LookaheadTokens);
                    var liftedContextResult = this.TrySolve(liftedContext);
                    if (!liftedContextResult.IsSuccessful) { return liftedContextResult.ErrorContext; }
                    return new DelegateToContextAction(liftedContext);
                }
            }

            // todo discriminator
            throw new NotImplementedException();
        }

        private ParsingContext CreateContext(IEnumerable<PotentialParseParentNode> nodes)
        {
            var nodesArray = nodes.ToArray();
            return new ParsingContext(
                nodesArray,
                nodesArray.Select(this.GetNextSetFromCursor).Aggregate((s1, s2) => s1.Union(s2))
            );
        }

        private ImmutableHashSet<Token> GetNextSetFromCursor(PotentialParseParentNode node)
        {
            if (node.HasTrailingCursor())
            {
                return this._firstFollow.FollowOf(node.Rule);
            }

            var result = ImmutableHashSet.CreateBuilder<Token>();
            if (GatherNextSetFromCursor(node))
            {
                result.UnionWith(this._firstFollow.FollowOf(node.Rule));
            }
            result.Remove(null);
            return result.ToImmutable();

            bool GatherNextSetFromCursor(PotentialParseNode current)
            {
                if (current is PotentialParseParentNode parent)
                {
                    for (var i = parent.CursorPosition ?? 0; i < parent.Children.Count; ++i)
                    {
                        if (!GatherNextSetFromCursor(parent.Children[i]))
                        {
                            return false; // found non-nullable symbol; stop
                        }
                    }

                    return true; // all children were nullable; continue to the next sibling of parent
                }

                var firstSet = this._firstFollow.FirstOf(current.Symbol);
                result.UnionWith(firstSet);
                return firstSet.Contains(null); // keep going if the symbol we just examined was nullable
            }
        }

        private struct ParserGenerationResult
        {
            public static ParserGenerationResult Success => default;

            public ParserGenerationResult(ParsingContext errorContext)
            {
                this.ErrorContext = errorContext;
            }

            public bool IsSuccessful => this.ErrorContext == null;

            public ParsingContext ErrorContext { get; }
        }

        private struct ParsingActionOrError
        {
            private ParsingActionOrError(ParsingAction action, ParsingContext errorContext)
            {
                this.Action = action;
                this.ErrorContext = errorContext;
            }

            public ParsingAction Action { get; }
            public ParsingContext ErrorContext { get; }

            public static implicit operator ParsingActionOrError(ParsingAction action) => new ParsingActionOrError(action, null);
            public static implicit operator ParsingActionOrError(ParsingContext errorContext) => new ParsingActionOrError(null, errorContext);
        }

        // todo copied method
        private static PotentialParseParentNode DefaultParseOf(Rule rule)
        {
            return new PotentialParseParentNode(rule, rule.Symbols.Select(s => new PotentialParseLeafNode(s)));
        }
        
        private static PotentialParseParentNode AdvanceCursor(PotentialParseParentNode node, int leafCount)
        {
            Invariant.Require(leafCount > 0);

            var oldCursorLeafIndex = GetCursorLeafIndex(node);
            var newCursorLeafIndex = oldCursorLeafIndex + leafCount;
            Invariant.Require(newCursorLeafIndex <= node.LeafCount);

            var updated = Update(node, baseLeafIndex: 0);
            if (newCursorLeafIndex == node.LeafCount)
            {
                updated = updated.WithTrailingCursor();
            }
            return (PotentialParseParentNode)updated;

            PotentialParseNode Update(PotentialParseNode toUpdate, int baseLeafIndex)
            {
                // check if this node could possibly overlap one of the indices of interest
                var maxLeafIndexExclusive = baseLeafIndex + toUpdate.LeafCount;
                if (!(baseLeafIndex <= oldCursorLeafIndex && oldCursorLeafIndex < maxLeafIndexExclusive)
                    && !(baseLeafIndex <= newCursorLeafIndex && newCursorLeafIndex < maxLeafIndexExclusive))
                {
                    return toUpdate;
                }

                if (toUpdate is PotentialParseParentNode parent)
                {
                    var childBaseLeafIndex = baseLeafIndex;
                    PotentialParseNode[] newChildren = null;
                    for (var i = 0; i < parent.Children.Count; ++i)
                    {
                        var child = parent.Children[i];
                        var updatedChild = Update(child, childBaseLeafIndex);
                        if (updatedChild != child && newChildren == null)
                        {
                            newChildren = new PotentialParseNode[parent.Children.Count];
                            for (var j = 0; j < i; ++j)
                            {
                                newChildren[j] = parent.Children[j];
                            }
                        }
                        if (newChildren != null)
                        {
                            newChildren[i] = updatedChild;
                        }

                        childBaseLeafIndex += child.LeafCount;
                    }

                    return newChildren != null ? new PotentialParseParentNode(parent.Rule, newChildren) : parent;
                }

                return baseLeafIndex == oldCursorLeafIndex ? toUpdate.WithoutCursor()
                    : baseLeafIndex == newCursorLeafIndex ? toUpdate.WithCursor(0)
                    : toUpdate;
            }
        }

        // todo copied method
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

        private bool TryLiftSymbolAtCursor(PotentialParseParentNode node, out PotentialParseParentNode[] lifted)
        {
            var pathToCursor = GetPathToCursor(node).ToArray();
            var cursorNode = pathToCursor[pathToCursor.Length - 1];
            if (pathToCursor.Take(pathToCursor.Length - 1).Any(n => n.Symbol == cursorNode.Symbol))
            {
                lifted = null;
                return false;
            }

            lifted = this._rulesByProduced[(NonTerminal)cursorNode.Symbol]
                .Select(r => ReplaceLeafInNode(node, GetCursorLeafIndex(node), this._defaultParses[r]))
                .ToArray();
            return true;
        }

        private static IEnumerable<PotentialParseNode> GetPathToCursor(PotentialParseNode node)
        {
            var current = node;
            while (true)
            {
                yield return current;

                if (current is PotentialParseParentNode parent)
                {
                    current = parent.Children[current.CursorPosition.Value];
                }
                else
                {
                    break;
                }
            }
        }

        // todo ADAPTED method from unifier
        private static PotentialParseParentNode ReplaceLeafInNode(PotentialParseNode node, int leafIndexInNode, PotentialParseParentNode replacement)
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
                        var childWithLeafReplaced = ReplaceLeafInNode(child, adjustedLeafIndex, replacement);

                        // we need to handle the edge case when the original child had a non-trailing cursor and
                        // the new child has a trailing cursor, which happens when replacing a cursor node with
                        // a null production
                        if (!child.HasTrailingCursor() && childWithLeafReplaced.HasTrailingCursor())
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
                                    parent.Children.Select((ch, index) => index == i ? childWithLeafReplaced.WithoutCursor() : index == indexToAddCursor ? parent.Children[index].WithCursor(0) : parent.Children[index])
                                );
                            }
                        }
                        return new PotentialParseParentNode(parent.Rule, parent.Children.Select((ch, index) => index == i ? childWithLeafReplaced : parent.Children[index]));
                    }

                    adjustedLeafIndex -= child.LeafCount;
                }
            }

            Invariant.Require(leafIndexInNode == 0 && node.Symbol == replacement.Symbol);
            Invariant.Require(node.CursorPosition.HasValue == replacement.CursorPosition.HasValue);
            Invariant.Require(!node.HasTrailingCursor() || replacement.HasTrailingCursor());
            return replacement;
        }
    }
}
