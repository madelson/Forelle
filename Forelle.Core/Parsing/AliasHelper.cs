using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text;
using System.Linq;

namespace Forelle.Parsing
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

            return symbolReferences.Where(g => g.Count() == 1 && g.Single().Symbols.Count == 1)
                .ToDictionary(g => g.Key, g => g.Single().Produced);
        }
    }
}
