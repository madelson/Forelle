using System;
using System.Collections.Generic;
using System.Text;

namespace Forelle.Parsing
{
    /// <summary>
    /// <see cref="SyntheticSymbolInfo"/> for a <see cref="Symbol"/> that represents
    /// treating the given <see cref="NonTerminal"/> as the start symbol for a parse
    /// </summary>
    internal sealed class StartSymbolInfo : SyntheticSymbolInfo
    {
        public StartSymbolInfo(Token endToken)
        {
            this.EndToken = endToken ?? throw new ArgumentNullException(nameof(endToken));
        }

        public Token EndToken { get; }
        public NonTerminal Symbol => ((EndSymbolTokenInfo)this.EndToken.SyntheticInfo).Symbol;
    }

    internal sealed class EndSymbolTokenInfo : SyntheticSymbolInfo
    {
        public EndSymbolTokenInfo(NonTerminal symbol)
        {
            this.Symbol = symbol ?? throw new ArgumentNullException(nameof(symbol));
        }

        public NonTerminal Symbol { get; }
    }
}
