using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text;
using System.Linq;
using Medallion.Collections;

namespace Forelle.Parsing.Preprocessing
{
    internal static class AliasHelper
    {
        /// <summary>
        /// Discovers all "alias" relationships between symbols. A symbol A is an alias of another symbol S if
        /// it is referenced only once and that reference is a production of the form S -> A
        /// </summary>
        public static IReadOnlyDictionary<NonTerminal, NonTerminal> FindAliases(IReadOnlyList<Rule> rules)
        {
            var symbolReferences = rules.SelectMany(r => r.Symbols.OfType<NonTerminal>(), (r, s) => (rule: r, symbol: s))
                .ToLookup(t => t.symbol, t => t.rule);

            return symbolReferences.Where(g => g.Count() == 1 && g.Single().Symbols.Count == 1 && g.Single().Produced != g.Key)
                .ToDictionary(g => g.Key, g => g.Single().Produced);
        }

        /// <summary>
        /// Determines whether <paramref name="alias"/> is an alias of <paramref name="aliased"/> by following the chain
        /// of aliases
        /// </summary>
        public static bool IsAliasOf(NonTerminal alias, NonTerminal aliased, IReadOnlyDictionary<NonTerminal, NonTerminal> aliases)
        {
            return aliases.TryGetValue(alias, out var directAliased)
                && (
                    directAliased == aliased
                    || IsAliasOf(directAliased, aliased, aliases)
                );
        }

        /// <summary>
        /// Rewrites the given grammar so that all alias symbols are inlined into the rule set of the symbol they alias.
        /// This simplifies the process of rewriting left-recursive rules
        /// 
        /// For example, if we have:
        /// 
        /// Exp -> Id
        /// Exp -> BinOp
        /// 
        /// BinOp -> Exp * Exp
        /// BinOp -> Exp + Exp
        /// 
        /// We will rewrite to:
        /// 
        /// Exp -> Id
        /// Exp -> Exp * Exp { PARSE AS { BinOp -> Exp * Exp, Exp -> BinOp } }
        /// Exp -> Exp + Exp { PARSE AS { BinOp -> Exp + Exp, Exp -> BinOp } }
        /// </summary>
        public static IReadOnlyList<Rule> InlineAliases(IReadOnlyList<Rule> rules, IReadOnlyDictionary<NonTerminal, NonTerminal> aliases)
        {
            if (aliases.Count == 0) { return rules; }

            // if a is an alias of b, we will inline a before b so that a gets inlined 
            // into b before b gets inlined into something
            var orderedAliasSymbols = TopologicallySortAliases(aliases); // todo test for circular aliases in validator

            var rulesByProduced = rules.GroupBy(r => r.Produced)
                .ToDictionary(g => g.Key, g => g.ToList());
            foreach (var alias in orderedAliasSymbols)
            {
                var aliasSymbolRules = rulesByProduced[alias];
                var aliased = aliases[alias];
                var aliasedSymbolRules = rulesByProduced[aliased]; 
                
                // remove all rules which produce the alias
                rulesByProduced.Remove(alias);

                // find the rule that creates the alias
                var aliasingRuleIndex = aliasedSymbolRules.FindIndex(r => r.Symbols.Count == 1 && r.Symbols[0] == alias);
                var aliasingRule = aliasedSymbolRules[aliasingRuleIndex];

                // update the mapping to remove the aliasing rule and remap the alias rules
                
                // replace the aliasing rule with the inlined alias rules in the rules for the aliased symbol
                aliasedSymbolRules.RemoveAt(aliasingRuleIndex);
                var inlinedRules = aliasSymbolRules.Select(
                    r => new Rule(aliased, r.Symbols, r.ExtendedInfo.Update(mappedRules: new[] { r, aliasingRule }))
                );
                aliasedSymbolRules.InsertRange(aliasingRuleIndex, inlinedRules);
            }

            return rulesByProduced.SelectMany(kvp => kvp.Value).ToList();
        }

        /// <summary>
        /// Performs a topological sort of alias symbols, using "B is an alias of A" to mean "B must come before A"
        /// </summary>
        private static IEnumerable<NonTerminal> TopologicallySortAliases(IReadOnlyDictionary<NonTerminal, NonTerminal> aliases)
        {
            var remaining = new HashSet<NonTerminal>(aliases.Keys);

            while (remaining.Count > 0)
            {
                var nextWithNoDependencyRemaining = remaining.Where(s => !remaining.Any(r => aliases[r] == s))
                    // break ties consistently
                    .MinBy(s => s.Name);
                yield return nextWithNoDependencyRemaining;
                remaining.Remove(nextWithNoDependencyRemaining);
            }
        }
    }
}
