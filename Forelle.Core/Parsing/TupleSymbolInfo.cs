using System;
using System.Collections.Generic;
using System.Text;

namespace Forelle.Parsing
{
    /// <summary>
    /// <see cref="SyntheticSymbolInfo"/> for a simple synthetic symbol that just has a
    /// single <see cref="Rule"/> which matches the element <see cref="Symbol"/>s of the tuple
    /// </summary>
    internal class TupleSymbolInfo : SyntheticSymbolInfo
    {
        private TupleSymbolInfo(IEnumerable<Symbol> elements)
        {
            this.Elements = Guard.NotNullOrContainsNullAndDefensiveCopy(elements, nameof(elements));
        }

        public IReadOnlyList<Symbol> Elements { get; }

        public static Rule CreateRule(IReadOnlyList<Symbol> elements, int existingEquivalentTupleCount, ExtendedRuleInfo extendedInfo = null)
        {
            var suffix = existingEquivalentTupleCount == 0 ? string.Empty : $"_{existingEquivalentTupleCount + 1}";
            var symbol = NonTerminal.CreateSynthetic(
                $"Tuple{suffix}<{string.Join(", ", elements)}>",
                new TupleSymbolInfo(elements)
            );
            return new Rule(symbol, elements, extendedInfo);
        }
    }
}
