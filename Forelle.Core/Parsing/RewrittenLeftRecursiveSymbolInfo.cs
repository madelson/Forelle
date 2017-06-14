using System;
using System.Collections.Generic;
using System.Text;

namespace Forelle.Parsing
{
    internal sealed class RewrittenLeftRecursiveSymbolInfo : SyntheticSymbolInfo
    {
        public RewrittenLeftRecursiveSymbolInfo(Rule replaced, RewrittenLeftRecursiveSymbolKind kind)
        {
            this.Replaced = replaced;
            this.Kind = kind;
        }

        public Rule Replaced { get; }
        public Rule Original => this.Replaced.Produced.SyntheticInfo is RewrittenLeftRecursiveSymbolInfo i ? i.Original : this.Replaced;

        public RewrittenLeftRecursiveSymbolKind Kind { get; }
    }

    internal enum RewrittenLeftRecursiveSymbolKind
    {
        NonRecursiveRemainder,
        LeftAssociativeSuffixList,
        LeftAssociativeSuffixListElement
    }
}
