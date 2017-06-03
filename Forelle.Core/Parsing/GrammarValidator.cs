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
        /// Validates a user-input grammar
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

            if (results.Any())
            {
                validationErrors = results;
                return false;
            }

            validationErrors = null;
            return true;
        }
    }
}
