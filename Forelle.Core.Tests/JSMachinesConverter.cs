using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Forelle.Tests
{
    /// <summary>
    /// Conversion to a format suitable for http://jsmachines.sourceforge.net/machines/lr1.html
    /// </summary>
    internal static class JSMachinesConverter
    {
        public static string ToJSMachinesInput(IReadOnlyList<Rule> rules, NonTerminal startSymbol = null) => string.Join(
            Environment.NewLine,
            new[] { $"S' -> {ToString(startSymbol ?? rules[0].Produced)}" }
                .Concat(rules.Select(r => $"{ToString(r.Produced)} -> {(r.Symbols.Any() ? string.Join(" ", r.Symbols.Select(ToString)) : "''")}"))
        );

        private static string ToString(Symbol symbol)
        {
            var name = symbol.Name.Replace(' ', '_');

            var lessThanIndex = name.IndexOf('<');
            if (lessThanIndex >= 0)
            {
                var greaterThanIndex = name.IndexOf('>', startIndex: lessThanIndex + 1);
                if (greaterThanIndex >= 0)
                {
                    return name.Replace('<', '[').Replace('>', ']');
                }
            }

            return name;
        }
    }
}
