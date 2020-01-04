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
        // todo if we start doing caching of default nodes for symbol/rule we can remove this
        private readonly IReadOnlyDictionary<Rule, PotentialParseParentNode> _defaultRuleNodes;

        private readonly Dictionary<LRClosure, Dictionary<Symbol, LRAction>> _parsingTable = new Dictionary<LRClosure, Dictionary<Symbol, LRAction>>();

        private LRGenerator(ILookup<NonTerminal, Rule> rulesByProduced, IFirstFollowProvider firstFollow)
        {
            this._rulesByProduced = rulesByProduced;
            this._firstFollow = firstFollow;

            this._defaultRuleNodes = rulesByProduced.SelectMany(g => g)
                .ToDictionary(r => r, r => (PotentialParseParentNode)PotentialParseNode.Create(r).WithCursor(0));
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
                            rule: new LRRule(this._defaultRuleNodes[this._rulesByProduced[t.symbol].Single()]),
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
            closure.Where(kvp => !kvp.Key.Node.HasTrailingCursor())
                .GroupBy(kvp => kvp.Key.Node.GetLeafAtCursorPosition().Symbol, kvp => (rule: new LRRule(kvp.Key.Node.AdvanceCursor()), lookahead: kvp.Value))
                .ToDictionary(g => g.Key, this.Closure);

        private IEnumerable<(Token token, LRReduceAction reduction)> Reductions(LRClosure closure) =>
            closure.Where(kvp => kvp.Key.Node.HasTrailingCursor())
                .SelectMany(kvp => kvp.Value.Tokens, (kvp, token) => (token, new LRReduceAction(kvp.Key.Node.Rule)));

        private LRClosure Closure(IEnumerable<(LRRule rule, LRLookahead lookahead)> kernelItems)
        {
            var closureBuilder = new Dictionary<LRRule, LRLookahead>();
            foreach (var item in kernelItems)
            {
                AddRule(item.rule, item.lookahead);
            }
            return new LRClosure(closureBuilder);
            
            void AddRule(LRRule rule, LRLookahead lookahead)
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

                if (changed 
                    && !rule.Node.HasTrailingCursor() 
                    && rule.Node.GetLeafAtCursorPosition().Symbol is NonTerminal nonTerminal)
                {
                    var remainderLookahead = this._firstFollow.FirstOf(GetLeavesFromCursor(rule.Node).Skip(1).Select(n => n.Symbol));
                    if (remainderLookahead.Contains(null))
                    {
                        remainderLookahead = remainderLookahead.Remove(null).Union(lookahead.Tokens);
                    }

                    foreach (var nonTerminalRule in this._rulesByProduced[nonTerminal])
                    {
                        AddRule(new LRRule(this._defaultRuleNodes[nonTerminalRule]), new LRLookahead(remainderLookahead));
                    }
                }
            }
        }

        private static IEnumerable<PotentialParseLeafNode> GetLeavesFromCursor(PotentialParseNode node) =>
            node.HasTrailingCursor()
                ? Enumerable.Empty<PotentialParseLeafNode>()
                : Traverse.DepthFirst(
                        (node, sawCursor: false),
                        t => t switch {
                            (node: PotentialParseParentNode parent, sawCursor: false) =>
                                parent.Children.SkipWhile(ch => !ch.CursorPosition.HasValue)
                                    .Select((ch, index) => (node: ch, sawCursor: index > 0)),
                            (node: PotentialParseParentNode parent, sawCursor: true) =>
                                parent.Children.Select(ch => (node: ch, sawCursor: true)),
                            _ => Enumerable.Empty<(PotentialParseNode node, bool sawCursor)>()
                        }
                    )
                    .Select(t => t.node)
                    .OfType<PotentialParseLeafNode>();
    }
}
