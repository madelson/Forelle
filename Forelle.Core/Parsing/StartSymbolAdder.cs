using System;
using System.Collections.Generic;
using System.Text;
using System.Linq;

namespace Forelle.Parsing
{
    internal static class StartSymbolAdder
    {
        /// <summary>
        /// Updates the provides list of <paramref name="rules"/> with start symbol rules such
        /// that for each <see cref="NonTerminal"/> N, we have a rule Start{N} => N End{N}.
        /// 
        /// Our parser will support parsing any N in the grammar; it will do so by appending End{N} to
        /// the token stream and then parsing Start{N}
        /// </summary>
        public static List<Rule> AddStartSymbols(IReadOnlyList<Rule> rules)
        {
            return rules.Concat(
                rules.Select(r => r.Produced)
                    .Distinct()
                    .Select(s => new Rule(
                        NonTerminal.CreateSynthetic($"Start<{s.Name}>", new StartSymbolInfo(s)),
                        s,
                        Token.CreateSynthetic($"End<{s.Name}", new EndSymbolTokenInfo(s))
                    ))
                )
                .ToList();
        }
    }
}
