using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ForellePlayground.Tests.LRInlining
{
    internal class Inliner
    {
        public static Rule[] Inline(
            Rule[] grammar, 
            FirstFollowCalculator firstFollow,
            Dictionary<Rule, HashSet<Token>> rulesToInline)
        {
            var symbolsToInline = rulesToInline.GroupBy(kvp => kvp.Key.Produced, kvp => kvp.Value)
                .ToDictionary(g => g.Key, g => g.Aggregate<IEnumerable<Token>>((s1, s2) => s1.Concat(s2)).ToHashSet());
            var rulesByProduced = grammar.ToLookup(r => r.Produced);

            List<Rule> result = new();
            foreach (var rule in grammar) { InlineHelper(new(rule, 0)); }
            return result.ToArray();

            void InlineHelper(MarkedRule rule)
            {
                if (rule.RemainingSymbols.IsEmpty)
                {
                    result.Add(rule.Rule);
                    return;
                }

                var nextSymbol = rule.RemainingSymbols[0];
                if (nextSymbol is NonTerminal nonTerminal
                    && symbolsToInline.TryGetValue(nonTerminal, out var lookahead)
                    && !firstFollow.NextOf(new(rule.Rule, rule.Position + 1)).Intersect(lookahead).IsEmpty)
                {
                    foreach (var inlinedRule in rulesByProduced[nonTerminal])
                    {
                        var inlined = rule.Rule.ReplaceDescendant(rule.Position, inlinedRule);
                        InlineHelper(new(inlined, rule.Position + inlinedRule.Descendants.Length));
                    }
                }
                else
                {
                    InlineHelper(new(rule.Rule, rule.Position + 1));
                }
            }
        }
    }
}
