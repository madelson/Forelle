<Query Kind="Program" />

#nullable enable
void Main()
{
	Case1();
}

void Case1()
{
	var Start = new NonTerminal("Start");
	var EOF = new Token("$");
	var a = new Token("a");
	var A = new NonTerminal("A");
	var grammar = new Rule[]
	{
		new(Start, A, EOF),
		new(A, a),
	};
	Parse(grammar, Start, new ParsedToken[] { new(a, "a"), new(EOF, "$") });
}

public ParsedSymbol Parse(Rule[] grammar, NonTerminal start, ParsedToken[] tokens)
{
	var rulesByProduced = grammar.ToLookup(r => r.Produced);
	
	var pos = 0;
	var R = new Stack<(RuleRemainder Slot, GSSNode Node, int Position)>();
	var U = new HashSet<(RuleRemainder Slot, GSSNode Node, int Position)>();
	var P = new HashSet<(GSSNode Node, int PopPosition)>();
	var nodes = new Dictionary<(RuleRemainder Slot, int Position), GSSNode>();
	Add(new(rulesByProduced[start].Single(), 0), new GSSNode(), pos);
	
	GSSNode currentNode = null!;	
	
	while (R.Count > 0)
	{
		var next = R.Pop();
		currentNode = next.Node;
		pos = next.Position;
		
		// handle currentNode
		var slot = next.Slot;
		var success = true;
		switch (slot.Symbols[0])
		{
			case Token token:
			{
				if (tokens[pos].Token == token)
				{
					$"Matched {token.Name}@{pos}".Dump();
					if (slot.Symbols.Length > 1)
					{
						Add(new(slot.Rule, slot.Position + 1), currentNode, pos);
					}
				}
				else { success = false; }
				break;
			}
			case NonTerminal nonTerminal:
			{
				foreach (var rule in rulesByProduced[nonTerminal])
				{
					Add(new(rule, 0), currentNode, pos);				
				}
				break;
			}
			default: throw new Exception();
		}
		if (success)
		{
			if (slot.Symbols.Length == 1)
			{
				$"Matched {slot.Rule.Produced.Name}@{pos}".Dump();
			}
			else 
			{
				Add(new(slot.Rule, slot.Position + 1), currentNode, pos);
				Pop();
			}
		}
	}
	return null!;
	
	void Create(RuleRemainder slot)
	{
		if (!nodes.TryGetValue((slot, pos), out var node))
		{
			nodes.Add((slot, pos), node = new());
		}
		if (!node.TryGetValue((slot, pos), out var edges))
		{
			node.Add((slot, pos), edges = new());
		}
		edges.Add(currentNode);
		
		if (P.Contains((node, pos))) { Add(slot, node, pos); }
	}
	
	void Add(RuleRemainder slot, GSSNode node, int position)
	{
		if (U.Add((slot, node, position))) { R.Push((slot, node, position)); }
	}
	
	void Pop()
	{
		// if (Cu != (⊥, 0) ) { ?
		P.Add((currentNode, pos));
		foreach (var (k, v) in currentNode)
		{
			foreach (var node in v)
			{
				Add(k.Slot, node, pos);
			}
		}
	}
}

public class GSSNode : Dictionary<(RuleRemainder Slot, int Position), HashSet<GSSNode>>
{
	public ParsedSymbol ParsedSymbol { get; set; }
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

public record ParsedSymbol(Symbol Symbol);
public record ParsedToken(Token Token, string Text) : ParsedSymbol(Token);
public record ParsedNonTerminal(Rule Rule, params ParsedSymbol[] Children) : ParsedSymbol(Rule.Produced);