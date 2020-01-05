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
        private readonly Dictionary<ParsingContext, ParserGenerationResult> _failedContextCache = new Dictionary<ParsingContext, ParserGenerationResult>();
        private readonly SubContextPlaceholderSymbolInfo.Factory _placeholderFactory = new SubContextPlaceholderSymbolInfo.Factory();

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
                    throw new NotImplementedException($"Failure context:{Environment.NewLine}{result.ErrorContext}");
                }
                Invariant.Require(this._stateStack.Count == 1 && !this._stateStack.Peek().HasPendingContexts);
            }

            var success = UnresolvedSubContextSwitchActionResolver.TryResolvePotentialSubParseSwitches(
                this._stateStack.Single().SolvedContexts,
                startSymbolContexts.Values,
                out var resolvedContexts);
            if (!success) { throw new NotImplementedException(); }

            return (startSymbolContexts, resolvedContexts);
        }

        private ParserGenerationResult TrySolve(ParsingContext context)
        {
            var currentState = this._stateStack.Peek();
            if (currentState.SolvedContexts.ContainsKey(context)) { return ParserGenerationResult.Success; }

            if (this._failedContextCache.TryGetValue(context, out var existingFailureResult))
            {
                return existingFailureResult;
            }

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

            var failureResult = new ParserGenerationResult(actionOrError.ErrorContext);
            this._failedContextCache.Add(context, failureResult);
            return failureResult;
        }

        private ParsingActionOrError TrySolveHelper(ParsingContext context)
        {
            var nodes = context.Nodes;

            // simplest case: we have one node with a trailing cursor: just reduce
            if (nodes.Count == 1 && nodes.Single().HasTrailingCursor())
            {
                return new ReduceAction(context, nodes);
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

                    return new TokenSwitchAction(context, subContexts.SelectMany(c => c.LookaheadTokens, (c, t) => (context: c, token: t)).ToDictionary(t => t.token, t => t.context));
                }
            }

            // if everything has a trailing cursor, then we're at a reduce-reduce conflict
            if (nodes.All(n => n.HasTrailingCursor()))
            {
                return new ReduceAction(context, nodes);
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
                var nextContext = this.CreateContext(nodes.Select(n => n.AdvanceCursor()));
                if (singleSymbol is Token token)
                {
                    var nextContextResult = this.TrySolve(nextContext);
                    if (!nextContextResult.IsSuccessful) { return nextContextResult.ErrorContext; }
                    return new EatTokenAction(context, token, next: nextContext);
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
                        return new ParseSubContextAction(context, nonTerminalContext, next: nextContext);
                    }
                }
            }

            // try specializing

            // first, see if we can solve any sub-contexts of our nodes. This is required for being able to handle cases
            // where specialization encounters recursion. It's not easy to pick out which sub-context is likely to be solvable,
            // so we simply try them all
            foreach (var potentialSubContext in SpecializationRecursionHelper.EnumerateSubContexts(context))
            {
                var subContextResult = this.TrySolve(potentialSubContext);
                if (subContextResult.IsSuccessful)
                {
                    var nodesToSubContextNodes = nodes.ToDictionary(
                        n => n,
                        n => potentialSubContext.Nodes.Where(subNode => subNode.IsCursorSubtreeOf(n)).MaxBy(subNode => subNode.CountNodes())
                    );
                    var nodesToNextNodes = nodesToSubContextNodes.ToDictionary(
                        kvp => kvp.Key,
                        kvp => SpecializationRecursionHelper.AdvanceCursorPastSubtree(kvp.Key, kvp.Value, this._placeholderFactory)
                    );

                    // at this point, we don't know whether the recursive context will be able to help us in discriminating
                    // between possible end parses. First, we construct the "minimal" next contexts which makes the most
                    // optimistic assumption about how the recursive context will be able to help us
                    var minimalNextContexts = nodesToSubContextNodes.GroupBy(
                            kvp => kvp.Value,
                            kvp => nodesToNextNodes[kvp.Key],
                            PotentialParseNodeWithCursorComparer.Instance
                        )
                        .Select(this.CreateContext)
                        .Distinct()
                        .ToArray();
                    foreach (var minimalNextContext in minimalNextContexts)
                    {
                        var nextResult = this.TrySolve(minimalNextContext);
                        // if we can't solve any one of the minimal next contexts, then we won't be 
                        // able to proceed under worse conditions
                        if (!nextResult.IsSuccessful) { return nextResult.ErrorContext; }
                    }

                    // otherwise, we may be able to learn something from the recursive context. So we will attempt
                    // to solve all combinations of the minimal contexts which might be required dependending on what
                    // the recursive context tells us
                    var solvableCombinedNextContexts = SpecializationRecursionHelper.GetAllCombinedContexts(minimalNextContexts)
                        .Where(c => this.TrySolve(c).IsSuccessful);
                    // we use an unresolved switch action here because we might not be able to tell right now what we could learn
                    // from our recursive context (it may depend on contexts we are still in the process of solving) and therefore 
                    // we don't know which next contexts we might need to handle
                    return new UnresolvedSubContextSwitchAction(
                        context,
                        subContext: potentialSubContext,
                        potentialNextContexts: solvableCombinedNextContexts,
                        @switch: nodesToSubContextNodes.Select(kvp => (subParseNode: kvp.Value, nextNode: nodesToNextNodes[kvp.Key])),
                        nextToCurrentNodeMapping: nodesToNextNodes.Select(kvp => (next: kvp.Value, current: kvp.Key))
                    );
                }
            }

            // todo try to specialize with a common required next nonterminal instead of a single token?

            // in order to specialize, we need a single lookahead token. If we have 
            // more than  one, branch on token
            if (context.LookaheadTokens.Count > 1)
            {
                var lookaheadTokensToSubContexts = context.LookaheadTokens.ToDictionary(t => t, t => new ParsingContext(nodes, ImmutableHashSet.Create(t)));
                foreach (var subContext in lookaheadTokensToSubContexts.Values)
                {
                    var subResult = this.TrySolve(subContext);
                    if (!subResult.IsSuccessful) { return subResult.ErrorContext; }
                }

                return new TokenSwitchAction(context, lookaheadTokensToSubContexts);
            }

            // if we get here and all nodes are recursive, then we're stuck! We could further specialize,
            // but because we're recursive we aren't going to find any new sub-contexts that could help us
            // escape from the recursion
            if (nodes.All(SpecializationRecursionHelper.HasRecursiveExpansion))
            {
                return context;
            }

            // see if we can specialize
            var specializations = new List<(PotentialParseParentNode next, PotentialParseParentNode current)>();
            var lookahead = context.LookaheadTokens.Single();
            var anySpecialized = false;
            foreach (var node in nodes)
            {
                var nodeSpecializations = this.Specialize(node, lookahead);
                anySpecialized = anySpecialized 
                    || nodeSpecializations.Count > 1 
                    || !PotentialParseNodeWithCursorComparer.Instance.Equals(node, nodeSpecializations[0]);
                specializations.AddRange(nodeSpecializations.Select(s => (next: s, current: node)));
            }
            
            if (!anySpecialized) // all specializations resulted in noops
            {
                return context;
            }

            var specializedContext = new ParsingContext(specializations.Select(t => t.next), context.LookaheadTokens);
            var specializedContextResult = this.TrySolve(specializedContext);
            if (!specializedContextResult.IsSuccessful) { return specializedContextResult.ErrorContext; }
            return new DelegateToSpecializedContextAction(context, specializedContext, specializations);
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
        
        private IReadOnlyList<PotentialParseParentNode> Specialize(
            PotentialParseParentNode node, 
            Token lookahead)
        {
            var result = Specialize(node, ImmutableLinkedList<PotentialParseLeafNode>.Empty)
                .Cast<PotentialParseParentNode>()
                .ToArray();
            Invariant.Require(result.Length > 0);
            return result;

            IEnumerable<PotentialParseNode> Specialize(PotentialParseNode current, ImmutableLinkedList<PotentialParseLeafNode> cursorFollowNodes)
            {
                // if we have a trailing cursor, we can't further expand
                if (current.HasTrailingCursor()) { return new[] { current }; }

                // if we have a parent node, expand the cursor position
                if (current is PotentialParseParentNode parent)
                {
                    var cursorPosition = current.CursorPosition.Value;

                    // tack on all nodes following the cursor
                    var childCursorFollowNodes = cursorFollowNodes.PrependRange(
                        Enumerable.Range(cursorPosition + 1, count: parent.Children.Count - (cursorPosition + 1))
                            // note: this cast must succeed since currently we always specialize a node from beginning to end,
                            // so all non-leaf nodes are behind or at the cursor
                            .Select(i => (PotentialParseLeafNode)parent.Children[i])
                    );

                    var childWithCursor = parent.Children[cursorPosition];
                    var childWithCursorSpecializations = Specialize(childWithCursor, childCursorFollowNodes);
                    return childWithCursorSpecializations.SelectMany(childSpecialization =>
                    {
                        // if specialization was a noop, noop
                        if (childSpecialization == childWithCursor) { return new[] { current }; }

                        // a trailing cursor indicates that the expansion resulted in a null child at the 
                        // trailing edge. In this case, we move the cursor forwards and keep trying to specialize
                        if (childSpecialization.HasTrailingCursor())
                        {
                            var updatedCurrent = new PotentialParseParentNode(
                                parent.Rule,
                                parent.Children.Select((ch, index) =>
                                {
                                    if (index == cursorPosition)
                                    {
                                        // if the cursor was already the last index, just leave the
                                        // child with trailing cursor; there's nowhere else to move it
                                        return index == parent.Children.Count - 1
                                            ? childSpecialization
                                            : childSpecialization.WithoutCursor(); // otherwise strip the child of its cursor
                                    }
                                    if (index == cursorPosition + 1)
                                    {
                                        return ch.WithCursor(0); // give the cursor to the next child
                                    }
                                    return ch;
                                })
                            );
                            return Specialize(updatedCurrent, cursorFollowNodes);
                        }

                        // child's cursor isn't trailing, so child now must have its cursor set to the lookahead token.
                        // So, just replace the original child with the new child
                        return new[]
                        {
                            new PotentialParseParentNode(
                                parent.Rule,
                                parent.Children.Select((ch, index) => index == cursorPosition ? childSpecialization : ch)
                            )
                        };
                    });
                }
                
                if (current.Symbol == lookahead)
                {
                    return new[] { current }; // cursor already on lookahead token => noop
                }

                // when we get here, we must have a non-terminal since any token would have
                // to have been the lookahead (caught above). Here, we expand the non-terminal
                // using each rule where it can appear
                var expansionRules = this._rulesByProduced[(NonTerminal)current.Symbol]
                    .Where(IsValidExpansionRule);
                return expansionRules.Select(r => this._defaultParses[r])
                    .SelectMany(n => Specialize(n, cursorFollowNodes));

                // a rule is valid if the lookahead token can be found in the first
                // of the rule symbols, the first of the cursor follow symbols, or
                // the follow of the top-most rule
                bool IsValidExpansionRule(Rule rule)
                {
                    for (var i = 0; i < rule.Symbols.Count; ++i)
                    {
                        var first = this._firstFollow.FirstOf(rule.Symbols[i]);
                        if (first.Contains(lookahead)) { return true; }
                        if (!first.Contains(null)) { return false; }
                    }

                    foreach (var followNode in cursorFollowNodes)
                    {
                        var first = this._firstFollow.FirstOf(followNode.Symbol);
                        if (first.Contains(lookahead)) { return true; }
                        if (!first.Contains(null)) { return false; }
                    }

                    return this._firstFollow.FollowOf(node.Rule).Contains(lookahead);
                }
            }
        }
    }
}
