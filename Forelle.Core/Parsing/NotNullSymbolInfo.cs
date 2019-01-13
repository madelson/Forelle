using System;
using System.Collections.Generic;
using System.Text;

namespace Forelle.Parsing
{
    /// <summary>
    /// Info for a variant of <see cref="BaseSymbol"/> which can never parse as null
    /// </summary>
    internal class NotNullSymbolInfo : SyntheticSymbolInfo
    {
        public NotNullSymbolInfo(NonTerminal baseSymbol)
        {
            this.BaseSymbol = baseSymbol ?? throw new ArgumentNullException(nameof(baseSymbol));
        }

        public NonTerminal BaseSymbol { get; }

        public static NonTerminal CreateNotNullSymbol(NonTerminal symbol) =>
            NonTerminal.CreateSynthetic($"NotNull<{symbol}>", new NotNullSymbolInfo(symbol));
    }
}
