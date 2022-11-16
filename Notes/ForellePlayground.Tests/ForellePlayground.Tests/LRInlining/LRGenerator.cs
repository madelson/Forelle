using Medallion.Collections;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;

namespace ForellePlayground.Tests.LRInlining;

internal class LRGenerator
{
    public static ConditionalWeakTable<Rule, IReadOnlyDictionary<NonTerminal, Rule>> MergedRuleMapping = new();

    internal static readonly Token Accept = new("<ACCEPT>");

    private readonly Rule _startRule;
    private readonly ILookup<NonTerminal, Rule> _rulesByProduced; // todo optimize for enumeration allocation
    private readonly FirstFollowCalculator _firstFollow;
    private readonly IReadOnlyDictionary<NonTerminal, ImmutableHashSet<NonTerminal>> _mergedNonTerminals;

    private readonly Dictionary<IReadOnlyCollection<LRItem>, LRState> _statesByItems =
        new(EqualityComparers.GetCollectionComparer<LRItem>());
    private readonly Stack<LRItem> _kernelBuilder = new();
    private readonly HashSet<LRItem> _stateBuilder = new();
    
    public LRGenerator(Rule[] rules, Dictionary<IEnumerable<NonTerminal>, NonTerminal> merged)
    {
        this._startRule = rules[0];
        this._rulesByProduced = rules.ToLookup(r => r.Produced);
        this._firstFollow = FirstFollowCalculator.Create(rules);
        this._mergedNonTerminals = merged.ToDictionary(kvp => kvp.Value, kvp => ImmutableHashSet.CreateRange(kvp.Key));
    }

    public static LRState[] Generate(Rule[] rules)
    {
        Dictionary<IEnumerable<NonTerminal>, NonTerminal> mergedSymbols = new(EqualityComparers.GetCollectionComparer<NonTerminal>());

        while (true)
        {
            LRGenerator generator = new(rules, mergedSymbols);
            var states = generator.Generate();
            var mergedSymbolCount = mergedSymbols.Count;
            var conflicts = generator.GetConflicts(mergedSymbols);

            if (conflicts.Count == 0 && mergedSymbols.Count == mergedSymbolCount) { return states; }

            rules = Inliner.Inline(rules, generator._firstFollow, conflicts);
        }
    }

    public LRState[] Generate()
    {
        this._kernelBuilder.Push(new(new(this._startRule, 0), Accept));
        var startState = this.ClosureFromKernelBuilder(out _);
        
        var statesToProcess = new Stack<LRState>();
        statesToProcess.Push(startState);

        while (statesToProcess.TryPop(out var state))
        {
            foreach (var item in state.Items)
            {
                if (item.Rule.RemainingSymbols.IsEmpty)
                {
                    state.TryAddAction(item.Lookahead, new Reduce(item.Rule.Rule));
                }
                else
                {
                    var gotoState = this.Goto(state, item.Rule.RemainingSymbols[0], out var createdNew);
                    if (createdNew) { statesToProcess.Push(gotoState); }
                }
            }

            foreach (var merged in this._mergedNonTerminals.Keys)
            {
                var gotoState = this.GotoMergedNonTerminal(state, merged, out var createdNew);
                if (createdNew) { statesToProcess.Push(gotoState!); }
            }
        }

        return this._statesByItems.Values.OrderBy(v => v.Id).ToArray();
    }

    private Dictionary<Rule, HashSet<Token>> GetConflicts(Dictionary<IEnumerable<NonTerminal>, NonTerminal> mergedSymbols)
    {
        var inlinedRules = new Dictionary<Rule, HashSet<Token>>();
        Queue<(LRState State, Symbol Symbol, Reduce NewReduce)> changes = new();

        foreach (var state in this._statesByItems.Values)
        {
            foreach (var symbol in state.SymbolsWithActions)
            {
                var actions = state.GetActions(symbol);
                if (actions.Length > 1)
                {
                    var reductions = actions.ToArray().OfType<Reduce>().ToArray();
                    // shift/reduce conflict: inline the reductions (TODO will not work with left recursion?)
                    if (reductions.Length < actions.Length)
                    {
                        foreach (var reduction in reductions) { AddInlinedRule(reduction.Rule); }
                    }
                    else
                    {
                        var lengths = reductions.Select(r => r.Rule.Descendants.Length).Distinct().ToArray();
                        // reduce/reduce with different lengths: inline the shorter ones
                        if (lengths.Length > 1)
                        {
                            var maxLength = lengths.Max();
                            foreach (var reduction in reductions.Where(r => r.Rule.Descendants.Length != maxLength))
                            {
                                AddInlinedRule(reduction.Rule);
                            }
                        }
                        // ambiguity detected
                        else if (reductions.Select(r => r.Rule.Produced).Distinct().Count() < reductions.Length)
                        {
                            throw new InvalidOperationException("AMBIGUITY"); // todo
                        }
                        // reduce/reduce with same length, different produced
                        else
                        {
                            // note: this is not a general condition; just something to try it out
                            if (reductions.All(r => r.Rule.Descendants.IsEmpty || r.Rule.Descendants.Contains(r.Rule.Produced)))
                            {
                                if (!mergedSymbols.TryGetValue(reductions.Select(r => r.Rule.Produced), out var mergedSymbol))
                                {
                                    var nonTerminals = reductions.Select(r => r.Rule.Produced).OrderBy(s => s.Name).ToArray();
                                    mergedSymbols.Add(nonTerminals, mergedSymbol = new(string.Join("|", (object?[])nonTerminals)));
                                }

                                // todo very incomplete: does not preserve nesting, does not account for different structures
                                var mergedRuleSymbols = reductions[0].Rule.DescendantsList.Select(s => s == reductions[0].Rule.Produced ? mergedSymbol : s).ToArray();
                                Rule mergedRule = new(mergedSymbol, mergedRuleSymbols); // may not matter what symbols this has so long as the count is right
                                MergedRuleMapping.Add(mergedRule, reductions.ToDictionary(r => r.Rule.Produced, r => r.Rule));
                                changes.Enqueue((state, symbol, new Reduce(mergedRule)));
                                continue;
                            }

                            foreach (var reduction in reductions) { AddInlinedRule(reduction.Rule); }
                        }
                    }
                }
            }

            void AddInlinedRule(Rule rule)
            {
                var items = state.ItemsList.Where(i => i.Rule == new MarkedRule(rule, rule.Descendants.Length))
                    .ToArray();
                Invariant.Require(items.Length > 0);
                if (!inlinedRules.TryGetValue(rule, out var lookaheadTokens))
                {
                    inlinedRules.Add(rule, lookaheadTokens = new());
                }
                foreach (var item in items) { lookaheadTokens.Add(item.Lookahead); }
            }
        }

        while (changes.TryDequeue(out var change))
        {
            change.State.ClearActions(change.Symbol);
            change.State.TryAddAction(change.Symbol, change.NewReduce);
        }

        return inlinedRules;
    }

    private LRState ClosureFromKernelBuilder(out bool createdNew)
    {
        Invariant.Require(this._kernelBuilder.Count > 0);
        Invariant.Require(this._stateBuilder.Count == 0);

        while (this._kernelBuilder.TryPop(out var item))
        {
            if (this._stateBuilder.Add(item)
                && !item.Rule.RemainingSymbols.IsEmpty
                && item.Rule.RemainingSymbols[0] is NonTerminal nonTerminal)
            {
                var lookahead = this._firstFollow.FirstOf(item.Rule.RemainingSymbols[1..], item.Lookahead);

                foreach (var rule in this._rulesByProduced[nonTerminal])
                {
                    foreach (var token in lookahead)
                    {
                        this._kernelBuilder.Push(new(new(rule, 0), token));
                    }
                }
            }
        }

        LRState result;
        if (this._statesByItems.TryGetValue(this._stateBuilder, out var existing))
        {
            createdNew = false;
            result = existing;
        }
        else
        {
            result = new(this._stateBuilder, this._statesByItems.Count);
            this._statesByItems.Add(result.ItemsList, result);
            createdNew = true;
        }

        this._stateBuilder.Clear();
        return result;
    }

    private LRState Goto(LRState state, Symbol symbol, out bool createdNew)
    {
        Invariant.Require(this._kernelBuilder.Count == 0);

        foreach (var item in state.Items)
        {
            if (!item.Rule.RemainingSymbols.IsEmpty && item.Rule.RemainingSymbols[0] == symbol)
            {
                this._kernelBuilder.Push(new(item.Rule.Advance(), item.Lookahead));
            }
        }

        var gotoState = this.ClosureFromKernelBuilder(out createdNew);
        state.TryAddAction(symbol, new Shift(gotoState));
        return gotoState;
    }

    private LRState? GotoMergedNonTerminal(LRState state, NonTerminal merged, out bool createdNew)
    {
        Invariant.Require(this._kernelBuilder.Count == 0);

        var baseSymbols = this._mergedNonTerminals[merged];
        var remainingBaseSymbols = baseSymbols;
        foreach (var item in state.Items)
        {
            if (!item.Rule.RemainingSymbols.IsEmpty
                && item.Rule.RemainingSymbols[0] is NonTerminal nonTerminal
                && baseSymbols.Contains(nonTerminal))
            {
                this._kernelBuilder.Push(new(item.Rule.Advance(), item.Lookahead));
                remainingBaseSymbols = remainingBaseSymbols.Remove(nonTerminal);
            }
        }

        if (remainingBaseSymbols.IsEmpty)
        {
            var gotoState = this.ClosureFromKernelBuilder(out createdNew);
            state.TryAddAction(merged, new Shift(gotoState));
            return gotoState;
        }

        this._kernelBuilder.Clear();
        createdNew = false;
        return null;
    }
}
