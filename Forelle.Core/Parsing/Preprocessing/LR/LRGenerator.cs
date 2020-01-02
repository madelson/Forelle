using Medallion.Collections;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;

namespace Forelle.Parsing.Preprocessing.LR
{
    // 1. run LR gen
    // 2. for each conflict, try to fix with expansion until we can't
    // 3. for each conflict, try to fix with reduce skipping until we can't

    internal class LRGenerator
    {
        private readonly ILookup<NonTerminal, Rule> _rulesByProduced;
        private readonly IFirstFollowProvider _firstFollow;

        private readonly Dictionary<LRClosure, Dictionary<Symbol, LRAction>> _parsingTable = new Dictionary<LRClosure, Dictionary<Symbol, LRAction>>();

        private LRGenerator(ILookup<NonTerminal, Rule> rulesByProduced, IFirstFollowProvider firstFollow)
        {
            this._rulesByProduced = rulesByProduced;
            this._firstFollow = firstFollow;
        }

        public static Dictionary<LRClosure, Dictionary<Symbol, LRAction>> Generate(ILookup<NonTerminal, Rule> rulesByProduced, IFirstFollowProvider firstFollow) =>
            new LRGenerator(rulesByProduced, firstFollow).Generate();

        private Dictionary<LRClosure, Dictionary<Symbol, LRAction>> Generate()
        {
            var startSymbols = this._rulesByProduced.Keys()
                .Where(s => s.SyntheticInfo is StartSymbolInfo);

            var closuresToProcess = new Queue<LRClosure>(
                // seed the processing queue with all start symbol rules
                this._rulesByProduced.Keys()
                    .Select(s => (symbol: s, startInfo: s.SyntheticInfo as StartSymbolInfo))
                    .Where(t => t.startInfo != null)
                    .Select(t => this.Closure(new[]
                    {
                        (
                            rule: this._rulesByProduced[t.symbol].Single().Skip(0),
                            lookahead: new LRLookahead(ImmutableHashSet<Token>.Empty)
                        )
                    }))
            );

            while (closuresToProcess.Count != 0)
            {
                var closureToProcess = closuresToProcess.Dequeue();
                if (this._parsingTable.ContainsKey(closureToProcess)) { continue; }

                var actions = new Dictionary<Symbol, LRAction>();
                this._parsingTable.Add(closureToProcess, actions);

                foreach (var kvp in this.Gotos(closureToProcess))
                {
                    actions.Add(kvp.Key, kvp.Key is Token ? new LRShiftAction(kvp.Value) : new LRGotoAction(kvp.Value).As<LRAction>());
                    closuresToProcess.Enqueue(kvp.Value);
                }

                foreach (var (token, reduction) in this.Reductions(closureToProcess))
                {
                    if (actions.TryGetValue(token, out var existing))
                    {
                        actions[token] = new LRConflictAction(existing, reduction);
                    }
                    else
                    {
                        actions.Add(token, reduction);
                    }
                }
            }

            return this._parsingTable;
        }

        private Dictionary<Symbol, LRClosure> Gotos(LRClosure closure) =>
            closure.Where(kvp => kvp.Key.Symbols.Count != 0)
                .GroupBy(kvp => kvp.Key.Symbols[0], kvp => (rule: kvp.Key.Skip(1), lookahead: kvp.Value))
                .ToDictionary(g => g.Key, this.Closure);

        private IEnumerable<(Token token, LRReduceAction reduction)> Reductions(LRClosure closure) =>
            closure.Where(kvp => kvp.Key.Symbols.Count == 0)
                .SelectMany(kvp => kvp.Value.Tokens, (kvp, token) => (token, new LRReduceAction(kvp.Key.Rule)));

        private LRClosure Closure(IEnumerable<(RuleRemainder rule, LRLookahead lookahead)> kernelItems)
        {
            var closureBuilder = new Dictionary<RuleRemainder, LRLookahead>();
            foreach (var item in kernelItems)
            {
                AddRule(item.rule, item.lookahead);
            }
            return new LRClosure(closureBuilder);
            
            void AddRule(RuleRemainder rule, LRLookahead lookahead)
            {
                bool changed;
                if (closureBuilder.TryGetValue(rule, out var existingLookahead))
                {
                    var newLookahead = new LRLookahead(lookahead.Tokens.Union(existingLookahead.Tokens));
                    if (!newLookahead.Equals(existingLookahead))
                    {
                        closureBuilder[rule] = newLookahead;
                        changed = true;
                    }
                    else
                    {
                        changed = false;
                    }
                }
                else
                {
                    closureBuilder.Add(rule, lookahead);
                    changed = true;
                }

                if (changed && rule.Symbols.Count != 0 && rule.Symbols[0] is NonTerminal nonTerminal)
                {
                    var remainderLookahead = this._firstFollow.FirstOf(rule.Skip(1).Symbols);
                    if (remainderLookahead.Contains(null))
                    {
                        remainderLookahead = remainderLookahead.Remove(null).Union(lookahead.Tokens);
                    }

                    foreach (var nonTerminalRule in this._rulesByProduced[nonTerminal])
                    {
                        AddRule(nonTerminalRule.Skip(0), new LRLookahead(remainderLookahead));
                    }
                }
            }
        }
    }
}
