using Medallion.Collections;

namespace ForellePlayground.Tests.LRInlining;

internal class LRGenerator
{
    internal static readonly Token Accept = new("<ACCEPT>");

    private readonly Rule _startRule;
    private readonly ILookup<NonTerminal, Rule> _rulesByProduced; // todo optimize for enumeration allocation
    private readonly FirstFollowCalculator _firstFollow;

    private readonly Dictionary<IReadOnlyCollection<LRItem>, LRState> _statesByItems =
        new(EqualityComparers.GetCollectionComparer<LRItem>());
    private readonly Stack<LRItem> _kernelBuilder = new();
    private readonly HashSet<LRItem> _stateBuilder = new();
    
    public LRGenerator(Rule[] rules)
    {
        this._startRule = rules[0];
        this._rulesByProduced = rules.ToLookup(r => r.Produced);
        this._firstFollow = FirstFollowCalculator.Create(rules);
    }

    //public static LRState[] Generate(Rule[] rules)
    //{
    //    while (true)
    //    {
    //        LRGenerator generator = new(rules);
    //        var states = generator.Generate();
    //        var conflicts = new Dictionary<Rule, HashSet<Token>>();
    //        foreach (var state in states)
    //        {
    //            foreach (var symbol in state.SymbolsWithActions)
    //            {
    //                var actions = state.GetActions(symbol);
    //                if (actions.Length > 1)
    //                {
    //                    var reductions = actions.ToArray().OfType<Reduce>().ToArray();
    //                    Invariant.Require(reductions.Length >= actions.Length - 1); // no shift-shift conflicts
    //                    foreach (var reduction in reductions)
    //                    {
    //                        var items = state.ItemsList.Where(i => i.Rule == new MarkedRule(reduction.Rule, reduction.Rule.Descendants.Length))
    //                            .ToArray();
    //                        Invariant.Require(items.Length > 0);
    //                        if (!conflicts.TryGetValue(reduction.Rule, out var lookaheadTokens))
    //                        {
    //                            conflicts.Add(reduction.Rule, lookaheadTokens = new());
    //                        }
    //                        foreach (var item in items) { lookaheadTokens.Add(item.Lookahead); }
    //                    }
    //                }
    //            }
    //        }

    //        if (conflicts.Count == 0) { return states; }


    //    }
    //}

    ////private Rule[] Inline(IReadOnlyList<Rule> rules, Dictionary<Rule, HashSet<Token>> conflicts)
    ////{
    ////    var inlinedNonTerminals = conflicts.GroupBy(kvp => kvp.Key.Produced)
    ////        .ToDictionary(
    ////            g => g.Key,
    ////            g => (
    ////                Lookahead: g.Select(kvp => kvp.Value.AsEnumerable()).Aggregate(Enumerable.Concat).ToHashSet(),
    ////                )
    ////}

    //private MarkedRule Inline(MarkedRule rule, Dictionary<NonTerminal, (Rule[] InlinedRules, HashSet<Token> Lookahead)> inlinedSymbols)
    //{

    //}

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
        }

        return this._statesByItems.Values.OrderBy(v => v.Id).ToArray();
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
}
