using System;
using System.Collections.Generic;
using System.Text;
using System.Linq;
using Medallion.Collections;

namespace Forelle.Parsing.Preprocessing
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

            var symbols = rules.GetAllSymbols();

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
            var recursionValidator = new RecursionValidator(rules);
            results.AddRange(recursionValidator.GetErrors());

            // validate parser state variable usage
            results.AddRange(GetParserVariableErrors(rules));

            if (results.Any())
            {
                validationErrors = results;
                return false;
            }

            validationErrors = null;
            return true;
        }

        private static List<string> GetParserVariableErrors(IReadOnlyCollection<Rule> rules)
        {
            var results = new List<string>();

            // rules must have only one action/check for any given variable
            results.AddRange(rules.SelectMany(
                r => r.ExtendedInfo.ParserStateActions.Select(a => a.VariableName)
                    .Concat(r.ExtendedInfo.ParserStateRequirements.Select(a => a.VariableName))
                    .GroupBy(v => v, (v, g) => (variable: v, count: g.Count()))
                    .Where(t => t.count > 1)
                    .Select(t => $"Rule {r} references variable '{t.variable}' multiple times. A rule may contain at most one check or one action for a variable")
            ));

            // each variable must have push, set, pop, and check
            const string Require = "REQUIRE";
            var requiredActions = new[] { ParserStateVariableActionKind.Push, ParserStateVariableActionKind.Set, ParserStateVariableActionKind.Pop }
                .Select(k => k.ToString().ToUpperInvariant())
                .Append(Require)
                .ToArray();
            results.AddRange(
                rules.SelectMany(r => r.ExtendedInfo.ParserStateActions.Select(a => (variable: a.VariableName, kind: a.Kind.ToString().ToUpperInvariant())))
                    .Concat(rules.SelectMany(r => r.ExtendedInfo.ParserStateRequirements.Select(t => (variable: t.VariableName, kind: Require))))
                    .GroupBy(t => t.variable, t => t.kind)
                    .Select(g => (variable: g.Key, missing: requiredActions.Except(g).ToArray()))
                    .Where(t => t.missing.Any())
                    .Select(t => $"Parser state variable '{t.variable}' is missing the following actions: [{string.Join(", ", t.missing)}]. Each parser state variable must define [{string.Join(", ", requiredActions)}]")
            );
            return results;
        }
    }
}
