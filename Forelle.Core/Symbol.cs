using System;
using System.Collections.Generic;
using System.Text;

namespace Forelle
{
    public abstract class Symbol
    {
        internal const char SyntheticMarker = '`';

        internal Symbol(string name, SyntheticSymbolInfo syntheticInfo)
        {
            if (string.IsNullOrEmpty(name)) { throw new ArgumentException("must not be null or empty", nameof(name)); }
            if (name.IndexOf(SyntheticMarker) >= 0) { throw new FormatException($"{nameof(name)}: must not contain '{SyntheticMarker}'"); }

            this.Name = syntheticInfo != null ? SyntheticMarker + name : name;
            this.SyntheticInfo = syntheticInfo;
        }

        public string Name { get; }
        internal SyntheticSymbolInfo SyntheticInfo { get; }
        internal bool IsSynthetic => this.SyntheticInfo != null;

        public override string ToString() => this.Name;
    }

    internal abstract class SyntheticSymbolInfo
    {
    }

    public sealed class Token : Symbol
    {
        private Token(string name, SyntheticSymbolInfo syntheticInfo)
            : base(name, syntheticInfo) { }

        public Token(string name)
            : this(name, syntheticInfo: null)
        {
        }

        internal static Token CreateSynthetic(string baseName, SyntheticSymbolInfo syntheticInfo)
        {
            return new Token(
                baseName,
                syntheticInfo: syntheticInfo ?? throw new ArgumentNullException(nameof(syntheticInfo))
            );
        }
    }

    public sealed class NonTerminal : Symbol
    {
        private NonTerminal(string name, SyntheticSymbolInfo syntheticInfo)
            : base(name, syntheticInfo) { }

        public NonTerminal(string name)
            : this(name, syntheticInfo: null)
        {
        }

        internal static NonTerminal CreateSynthetic(string baseName, SyntheticSymbolInfo syntheticInfo)
        {
            return new NonTerminal(
                baseName,
                syntheticInfo: syntheticInfo ?? throw new ArgumentNullException(nameof(syntheticInfo))
            );
        }
    }
}
