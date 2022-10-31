using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace ForellePlayground.Tests.LRInlining;

internal interface ISymbol 
{ 
    Symbol Symbol { get; }
}

internal abstract record Symbol(string Name) : ISymbol 
{
    Symbol ISymbol.Symbol => this;
    public sealed override string ToString() => this.Name; 
}

internal sealed record Token(string Name) : Symbol(Name);
internal record NonTerminal(string Name) : Symbol(Name);

internal sealed class Rule : ISymbol
{
    private readonly ISymbol[] _symbols;
    private Symbol[]? _cachedDescendants;
    private int _cachedHashCode;

    public Rule(NonTerminal produced, params ISymbol[] symbols)
    {
        this.Produced = produced;
        this._symbols = symbols.ToArray();
    }

    public NonTerminal Produced { get; }
    Symbol ISymbol.Symbol => this.Produced;
    public ReadOnlySpan<ISymbol> Symbols => this._symbols;
    public IReadOnlyList<ISymbol> SymbolsList => this._symbols;

    public ReadOnlySpan<Symbol> Descendants => this.DescendantsArray;
    public IReadOnlyList<Symbol> DescendantsList => this.DescendantsArray;
    private Symbol[] DescendantsArray => this._cachedDescendants ??= this.GetDescendants();

    private Symbol[] GetDescendants()
    {
        var descendants = new List<Symbol>();
        GatherDescendants(this);
        return descendants.ToArray();

        void GatherDescendants(Rule rule)
        {
            foreach (var symbol in this.Symbols)
            {
                if (symbol is Rule childRule)
                {
                    GatherDescendants(childRule);
                }
                else
                {
                    descendants.Add(symbol.Symbol);
                }
            }
        }
    }

    public override bool Equals(object? obj) => obj is Rule that
        && this.Produced == that.Produced
        && this.Symbols.SequenceEqual(that.Symbols);

    public override int GetHashCode()
    {
        if (this._cachedHashCode == 0)
        {
            HashCode hash = default;
            hash.Add(this.Produced);
            foreach (var symbol in this.Symbols) { hash.Add(symbol); }
            this._cachedHashCode = hash.ToHashCode();
        }
        return this._cachedHashCode;
    }

    public static bool operator ==(Rule? @this, Rule? that) => Equals(@this, that);
    public static bool operator !=(Rule? @this, Rule? that) => !(@this == that);

    public override string ToString() => $"{this.Produced}({string.Join(" ", (object[])this._symbols)})";
}

internal record struct MarkedRule(Rule Rule, int Position)
{
#if DEBUG
    public int Position { get; } = 
        Position >= 0 && Position <= Rule.Symbols.Length 
            ? Position 
            : throw new ArgumentOutOfRangeException(nameof(Position), Position, $"Must be in [0, {Rule.Symbols.Length}]");
#endif

    public ReadOnlySpan<Symbol> RemainingSymbols => this.Rule.Descendants[this.Position..];

    public MarkedRule Advance() => new(this.Rule, this.Position + 1);

    public override string ToString()
    {
        StringBuilder builder = new();
        var symbolsRemainingBeforePosition = this.Position;
        Write(this.Rule);
        WritePositionIfNeeded();
        return builder.ToString();

        void Write(Rule rule)
        {
            WritePositionIfNeeded();
            WriteSpaceIfNeeded();
            builder.Append(rule.Produced).Append('(');
            foreach (var symbol in rule.Symbols)
            {
                if (symbol is Rule childRule)
                {
                    Write(childRule);
                }
                else
                {
                    WritePositionIfNeeded();
                    WriteSpaceIfNeeded();
                    builder.Append(symbol);
                    --symbolsRemainingBeforePosition;
                }
            }
            builder.Append(')');
        }

        void WritePositionIfNeeded()
        {
            if (symbolsRemainingBeforePosition == 0)
            {
                WriteSpaceIfNeeded();
                builder.Append('•');
                --symbolsRemainingBeforePosition;
            }
        }

        void WriteSpaceIfNeeded()
        {
            if (builder.Length > 0 && builder[^1] != '(') { builder.Append(' '); }
        }
    }
}