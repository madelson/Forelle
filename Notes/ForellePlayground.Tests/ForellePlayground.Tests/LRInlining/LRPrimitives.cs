using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ForellePlayground.Tests.LRInlining;

internal record struct LRItem(MarkedRule Rule, Token Lookahead)
{
    public override string ToString() => $"{this.Rule} {{{this.Lookahead}}}";
}

internal sealed class LRState
{
    private readonly LRItem[] _items;
    private readonly Dictionary<Symbol, LRAction[]> _actions = new();

    public LRState(HashSet<LRItem> items, int id)
    {
        this._items = items.ToArray();
        this.Id = id;
    }

    public int Id { get; }
    public ReadOnlySpan<LRItem> Items => this._items;
    public IReadOnlyList<LRItem> ItemsList => this._items;

    public Dictionary<Symbol, LRAction[]>.KeyCollection SymbolsWithActions => this._actions.Keys;

    public bool TryAddAction(Symbol symbol, LRAction action)
    {
        if (!this._actions.TryGetValue(symbol, out var symbolActions))
        {
            this._actions.Add(symbol, new[] { action });
            return true;
        }

        if (Array.IndexOf(symbolActions, action) < 0)
        {
            var newSymbolActions = new LRAction[symbolActions.Length + 1];
            symbolActions.AsSpan().CopyTo(newSymbolActions);
            newSymbolActions[^1] = action;
            this._actions[symbol] = newSymbolActions;
            return true;
        }

        return false;
    }

    public ReadOnlySpan<LRAction> GetActions(Symbol symbol) =>
        this._actions.TryGetValue(symbol, out var symbolActions) ? symbolActions : ReadOnlySpan<LRAction>.Empty;

    public void ClearActions(Symbol symbol) => this._actions.Remove(symbol);
}

internal abstract record LRAction;
internal sealed record Shift(LRState Destination) : LRAction;
internal sealed record Reduce(Rule Rule) : LRAction;

//internal readonly struct LRAction : IEquatable<LRAction>
//{
//    private readonly object _value;

//    private LRAction(object value) { this._value = value; }

//    public static LRAction Shift(LRState destination) => new(destination);
//    public static LRAction Reduce(Rule rule) => new(rule);

//    public bool Equals(LRAction other) => Equals(this._value, other._value);
//    public override bool Equals([NotNullWhen(true)] object? obj) => obj is LRAction that && this.Equals(that);
//    public override int GetHashCode() => this._value.GetHashCode();

//    public static bool operator ==(LRAction @this, LRAction that) => @this.Equals(that);
//    public static bool operator !=(LRAction @this, LRAction that) => !(@this == that);

//    public bool IsShift([NotNullWhen(true)] out LRState? destination, [NotNullWhen(false)] out Rule? reduction)
//    {
//        if (this._value is LRState state)
//        {
//            destination = state;
//            reduction = null;
//            return true;
//        }

//        destination = null;
//        reduction = Unsafe.As<Rule>(this._value);
//        return false;
//    }

//    public override string ToString() => 
//        this.IsShift(out var destination, out var rule) ? $"SHIFT TO {destination.Id}" : $"REDUCE BY {rule}";
//}
