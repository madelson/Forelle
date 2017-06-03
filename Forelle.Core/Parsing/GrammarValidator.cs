using System;
using System.Collections.Generic;
using System.Text;
using System.Linq;
using Medallion.Collections;

namespace Forelle.Parsing
{
    internal static class GrammarValidator
    {
        /// <summary>
        /// Validates a user-input grammar, ruling out edge cases that would otherwise trip up the parser 
        /// generator and/or lead to extra edge-cases down the line
        /// </summary>
        public static bool Validate(IReadOnlyList<Rule> rules, out List<string> validationErrors)
        {
            var results = new List<string>();

            if (!rules.Any())
            {
                results.Add("At least one rule is required");
            }

            var symbols = new HashSet<Symbol>(rules.SelectMany(r => r.Symbols).Concat(rules.Select(r => r.Produced)));

            // check for name re-use
            results.AddRange(
                symbols.GroupBy(s => s.Name)
                    .Where(g => g.Count() > 1)
                    .Select(g => $"Multiple symbols found with the same name: '{g.Key}'")
            );

            // check for S -> S rules
            results.AddRange(
                rules.Where(r => r.Symbols.Count == 1 && r.Produced == r.Symbols[0])
                    .Select(r => $"Rule {r} was of invalid form S -> S")
            );

            // check for undefined non-terminals
            var rulesByProduced = rules.ToLookup(r => r.Produced);
            results.AddRange(
                symbols.OfType<NonTerminal>()
                    .Where(s => !rulesByProduced[s].Any())
                    .Select(s => $"No rules found that produce symbol '{s}'")
            );

            // check for duplicate rules
            // NOTE: we could relax this if the same rule is specified multiple times with different constraints
            var symbolsComparer = EqualityComparers.GetSequenceComparer<Symbol>();
            results.AddRange(
                rulesByProduced.SelectMany(g => g.GroupBy(gg => gg.Symbols, symbolsComparer))
                    .Where(g => g.Count() > 1)
                    .Select(g => $"Rule {g.First()} was specified multiple times")
            );

            // check for recursively-defined symbols
            results.AddRange(GetRecursionErrors(rulesByProduced));

            if (results.Any())
            {
                validationErrors = results;
                return false;
            }

            validationErrors = null;
            return true;
        }

        private static IEnumerable<string> GetRecursionErrors(ILookup<NonTerminal, Rule> rulesByProduced)
        {
            var nonRecursive = new HashSet<NonTerminal>();

            bool changed;
            do
            {
                changed = false;

                foreach (var symbolRules in rulesByProduced)
                {
                    // a symbol is non-recursive if it has any rule whose symbols are all non-recursive. A symbol is non-recursive
                    // if it is a Token, an established non-recursive NonTerminal, or an undefined NonTerminal (to avoid confusing errors)
                    if (symbolRules.Any(r => r.Symbols.All(s => s is Token || (s is NonTerminal n && (nonRecursive.Contains(n) || !rulesByProduced[n].Any())))))
                    {
                        changed |= nonRecursive.Add(symbolRules.Key);
                    }
                }
            }
            while (changed);

            return rulesByProduced.Select(g => g.Key)
                .Where(s => !nonRecursive.Contains(s))
                .Select(s => $"All rules for symbol '{s}' recursively contain '{s}'");
        }
    }
}
