<Query Kind="Program">
  <NuGetReference Version="1.1.0">MedallionPriorityQueue</NuGetReference>
  <Namespace>Medallion.Collections</Namespace>
  <IncludeLinqToSql>true</IncludeLinqToSql>
</Query>

#nullable enable
void Main()
{
}

public ParsedSymbol Parse(Rule[] grammar, NonTerminal start, Func<int, Context, ParsedToken> lexer)
{
	var rulesByProduced = grammar.ToLookup(r => r.Produced);
	var currentNodes = new Dictionary<GssNodeKey, GssNode>();
	var nodesToProcess = new PriorityQueue<GssNodeKey>(new ByLexerPositionComparer());
	// Stores nodes in creation order. Because nodesToProcess is a PQ by lexer position, this is close to lexer pos order.
	// However, it is not exactly lexer pos order because of context-dependent tokens. Being out of order is OK, though; it
	// should still be good enough to make CleanCurrentNodes() effective
	var currentNodeKeys = new Queue<GssNodeKey>();
	
	void CleanCurrentNodes()
	{
		if (nodesToProcess.Count == 0)
		{
			currentNodeKeys.Clear();
			currentNodes.Clear();
		}
		else 
		{
			var minActiveLexerPosition = nodesToProcess.Peek().LexerPosition;
			while (currentNodeKeys.Peek().LexerPosition < minActiveLexerPosition)
			{
				currentNodes.Remove(currentNodeKeys.Dequeue());
			}
		}
	}
	
	void Push(GssNodeKey key, GssNode? parent)
	{
		if (currentNodes.TryGetValue(key, out var existing))
		{
			if (parent != null && existing.Parents.Add(parent))
			{
				 // todo
			}
			else { return; }
		}

		GssNode newNode = new() { Key = key };
		if (parent != null) { newNode.Parents.Add(parent); }
		currentNodes.Add(key, newNode);
		currentNodeKeys.Enqueue(key);
		if (key.Slot.Symbols.Length == 0)
		{
			Reduce(newNode);
		}
		else if (key.Slot.Symbols[0] is NonTerminal nonTerminal)
		{
			foreach (var rule in rulesByProduced[nonTerminal])
			{
				Push(new(new(rule, 0), key.Context, key.LexerPosition), newNode);
			}
		}
	}
	
	void Reduce(GssNode node, ParsedSymbolList? siblings = null)
	{
		if (node.Key.Slot.Position == 0)
		{
			var children = new ParsedSymbol[node.Key.Slot.Rule.Symbols.Length];
			for (var i = children.Length - 1; i > 0; --i)
			{
				children[i] = siblings!.Symbol;
			}
			var parsed = new ParsedNonTerminal(node.Key.Slot.Rule, children);
			foreach (var parent in node.Parents)
			{
				
			}
		}
	}
	
	void Advance(GssNode node, ParsedSymbol parsed)
	{
		Debug.Assert(node.Key.Slot.Symbols[0] == parsed.Symbol);
		
	}
}

public class GssNode 
{
	public GssNodeKey Key { get; set; } = default!;
	public HashSet<GssNode> Parents { get; } = new();
	public ParsedSymbol? Parsed { get; set; }
}

public record ParsedSymbolList(ParsedSymbol Symbol, ParsedSymbolList? Next);

public record GssNodeKey(RuleRemainder Slot, Context Context, int LexerPosition);

private class ByLexerPositionComparer : IComparer<GssNodeKey>
{
	public int Compare(GssNodeKey? @this, GssNodeKey? that) => @this!.LexerPosition.CompareTo(that!.LexerPosition);
}

public class Context : Dictionary<ContextKey, ContextValue>
{
	public Context() : base() { }
	public Context(Context other) : base(other) { }

	public override bool Equals(object? obj)
	{
		return obj is Context that
			&& this.Count == that.Count
			&& this.OrderBy(kvp => kvp.Key.Name).SequenceEqual(that.OrderBy(kvp => kvp.Key.Name));
	}

	public override int GetHashCode() =>
		this.Aggregate(0, (hash, kvp) => hash ^ kvp.GetHashCode());
}

public record ContextKey(string Name, params string[] Values);
public record ContextValue(ContextKey Key, string Value);
public enum ContextAction { Push, Set, Pop }
public abstract record Symbol(string Name);
public record Token(string Name, params ContextValue[] ContextValues) : Symbol(Name);
public record NonTerminal(string Name) : Symbol(Name);
public record ContextActionNonTerminal(ContextKey Key, ContextAction Action, string? Value) : NonTerminal($"{Key}.{Action}.{Value}");
public record Rule(NonTerminal Produced, params Symbol[] Symbols);
public record RuleRemainder(Rule Rule, int Position) { public ReadOnlySpan<Symbol> Symbols => this.Rule.Symbols.AsSpan(this.Position); }

public abstract record ParsedSymbol(Symbol Symbol, int Width);
public record ParsedToken(Token Token, string Text) : ParsedSymbol(Token, Text.Length);
public record ParsedNonTerminal(Rule Rule, params ParsedSymbol[] Children) : ParsedSymbol(Rule.Produced, Children.Sum(ch => ch.Width));