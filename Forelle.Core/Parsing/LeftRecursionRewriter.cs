using Medallion.Collections;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Forelle.Parsing
{
    /// <summary>
    /// Rewrites simple left-recursive rules so that the grammar is not left-recursive
    /// </summary>
    internal class LeftRecursionRewriter
    {
        private readonly Dictionary<NonTerminal, List<Rule>> _rulesByProduced;

        private LeftRecursionRewriter(IReadOnlyList<Rule> rules)
        {
            this._rulesByProduced = rules.GroupBy(r => r.Produced)
                .ToDictionary(g => g.Key, g => g.ToList());
        }
        
        public static List<Rule> Rewrite(IReadOnlyList<Rule> rules)
        {
            var rewriter = new LeftRecursionRewriter(rules);
            while (rewriter.RewriteOne()) ;

            return rewriter._rulesByProduced.SelectMany(kvp => kvp.Value).ToList();
        }

        private bool RewriteOne()
        {
            // Example with all right associative (left-associative just replaces (+ E)? with (+ EX)*)
            // 
            // START
            // E = -E | E * E | E + E | ID
            //
            // ONE REWRITE
            // E = E0 (* E)? | E + E
            // E0 = -E0 | ID
            //
            // TWO REWRITES
            // E = E1 (+ E)?
            // E1 = E0 (* E)?
            // E0 = -E0 | ID

            // pick the highest precedence left-recursive rule we can find
            var leftRecursiveRule = this._rulesByProduced.SelectMany(kvp => kvp.Value)
                .FirstOrDefault(IsSimpleLeftRecursive);
            if (leftRecursiveRule == null) { return false; }

            // create a symbol which will represent the "remainder" of the rules for the left-recursive rule's produced
            // symbol (excluding the left-recursive rule itself)
            var existingSyntheticSymbolCount = this._rulesByProduced.Count(
                kvp => kvp.Key.SyntheticInfo is RewrittenLeftRecursiveSymbolInfo i 
                    && i.Kind == RewrittenLeftRecursiveSymbolKind.NonRecursiveRemainder 
                    && i.Original.Produced == leftRecursiveRule.Produced
            );
            var remainderSymbol = NonTerminal.CreateSynthetic(
                $"{leftRecursiveRule.Produced}_{existingSyntheticSymbolCount}",
                new RewrittenLeftRecursiveSymbolInfo(leftRecursiveRule, RewrittenLeftRecursiveSymbolKind.NonRecursiveRemainder)
            );
            this._rulesByProduced.Add(remainderSymbol, new List<Rule>());

            // determine which rules get pushed to the new symbol. These will be any rules that are neither simple left 
            // nor simple right-recursive as well as any simple right-recursive rules that are higher precedence than the current rule. 
            // The reason we need to move higher priority right-recursive rules is so that something like -E which will continue
            // to "bind tighter" than E + E
            var leftRecursiveRuleIndex = this._rulesByProduced[leftRecursiveRule.Produced].IndexOf(leftRecursiveRule);
            var rulesToMove = this._rulesByProduced[leftRecursiveRule.Produced]
                // this check is simpler than the requirement stated above, because we leverage the fact that we know the current
                // rule is the highest-precedence left-recursive rule for the symbol
                .Where((r, index) => index < leftRecursiveRuleIndex || (!IsSimpleLeftRecursive(r) && !IsSimpleRightRecursive(r)))
                .ToArray();
            foreach (var ruleToMove in rulesToMove)
            {
                this._rulesByProduced[leftRecursiveRule.Produced].Remove(ruleToMove);

                // note that right-recursive rules have their last symbol replaced by the new symbol as well. This is to indicate the fact
                // that E => -E can't parse as -(1 + 1) because unary minus binds tighter
                var movedRule = new Rule(
                    remainderSymbol,
                    ruleToMove.Symbols.Select((s, i) => i == ruleToMove.Symbols.Count - 1 && s == leftRecursiveRule.Produced ? remainderSymbol : s),
                    ruleToMove.ExtendedInfo.Update(mappedRules: new[] { ruleToMove })
                );
                this._rulesByProduced[remainderSymbol].Add(movedRule);
            }

            // next, replace the original left-recursive rule

            this._rulesByProduced[leftRecursiveRule.Produced].Remove(leftRecursiveRule);
            
            // a rule is left-associative if it is binary and is not marked as right-associative
            var isLeftAssociative = !leftRecursiveRule.ExtendedInfo.IsRightAssociative
                && IsSimpleRightRecursive(leftRecursiveRule);
            if (isLeftAssociative)
            {
                // for left-associativity, we do:
                // E -> E + E
                // E -> T List<+T>
                // every parse of "+ T" maps to E + E rule
                var suffixSymbol = NonTerminal.CreateSynthetic(
                    $"({string.Join(" ", leftRecursiveRule.Symbols.Skip(1).Take(leftRecursiveRule.Symbols.Count - 2).Append(remainderSymbol))})",
                    new RewrittenLeftRecursiveSymbolInfo(leftRecursiveRule, RewrittenLeftRecursiveSymbolKind.LeftAssociativeSuffixListElement)
                );
                var suffixListSymbol = NonTerminal.CreateSynthetic(
                    $"List<{suffixSymbol}>",
                    new RewrittenLeftRecursiveSymbolInfo(leftRecursiveRule, RewrittenLeftRecursiveSymbolKind.LeftAssociativeSuffixList)
                );

                this._rulesByProduced[leftRecursiveRule.Produced].InsertRange(
                    // the new rules are inserted at the front since we removed all rules higher-priority than
                    // the left-recursive rule. Thus we are replacing the first rule in the list
                    0,
                    new[]
                    {
                        // E -> T List<+T>
                        new Rule(leftRecursiveRule.Produced, new[] { remainderSymbol, suffixListSymbol }, leftRecursiveRule.ExtendedInfo.Update(mappedRules: Empty.Array<Rule>()))
                    }
                );

                this._rulesByProduced.Add(
                    suffixListSymbol,
                    new List<Rule>
                    {
                        // List<+T> -> +T List<+T>
                        new Rule(suffixListSymbol, new[] { suffixSymbol, suffixListSymbol }, ExtendedRuleInfo.Unmapped),
                        // List<+T> -> 
                        new Rule(suffixListSymbol, Enumerable.Empty<Symbol>(), ExtendedRuleInfo.Unmapped)
                    }
                );

                this._rulesByProduced.Add(
                    suffixSymbol,
                    // +T -> + T
                    new List<Rule>
                    {
                        new Rule(
                            suffixSymbol, 
                            leftRecursiveRule.Symbols.Select((s, i) => i == leftRecursiveRule.Symbols.Count - 1 ? remainderSymbol : s).Skip(1),
                            ExtendedRuleInfo.Empty.Update(mappedRules: new[] { leftRecursiveRule })
                        )
                    }
                );
            }
            else
            {
                // e. g. for E -> E ? E : E, we have E -> T | E -> T ? E : E
                // E -> T is unmapped, while E -> T ? E : E maps to the original rule

                this._rulesByProduced[leftRecursiveRule.Produced].InsertRange(
                    // the new rules are inserted at the front since we removed all rules higher-priority than
                    // the left-recursive rule. Thus we are replacing the first rule in the list
                    0,
                    new[]
                    {
                        new Rule(
                            leftRecursiveRule.Produced, 
                            new[] { remainderSymbol }, 
                            ExtendedRuleInfo.Unmapped
                        ),
                        new Rule(
                            leftRecursiveRule.Produced, 
                            leftRecursiveRule.Symbols.Select((s, i) => i == 0 ? remainderSymbol : s),
                            leftRecursiveRule.ExtendedInfo.Update(mappedRules: new[] { leftRecursiveRule })
                        )
                    }
                );
            }

            return true;
        }

        private static bool IsSimpleLeftRecursive(Rule rule) => rule.Symbols.Count > 0 && rule.Symbols[0] == rule.Produced;

        private static bool IsSimpleRightRecursive(Rule rule) => rule.Symbols.Count > 0 && rule.Symbols[rule.Symbols.Count - 1] == rule.Produced;
    }
}
