<Query Kind="Program">
  <NuGetReference>MedallionComparers</NuGetReference>
  <Namespace>Microsoft.CodeAnalysis</Namespace>
  <Namespace>System.Collections.Immutable</Namespace>
  <Namespace>System.Numerics</Namespace>
  <Namespace>System.Runtime.CompilerServices</Namespace>
  <Namespace>System.Runtime.InteropServices</Namespace>
  <Namespace>Medallion.Collections</Namespace>
</Query>

#nullable enable

void Main()
{
	
}

class AmbiguityCalculator
{	
	private readonly (SymbolNumber LookaheadToken, SymbolNumber[] FirstNonTerminals)[][] _lookaheadInfoByRuleRemainder;
	private readonly SymbolNumber[] _firstSymbolByRuleRemainder;	

	private readonly bool[] _isNonTerminalProcessed;
	private readonly PositionState[] _positionState;

	private readonly Rule[] _rulesByNumber;
	private readonly RuleRemainder[] _ruleRemaindersByNumber;
	private readonly Symbol[] _symbolsByNumber;

	public AmbiguityCalculator(Rule[] rules, int tokenCount)
	{
		var firstFollow = FirstFollowCalculator.Create(rules);
		
		var ruleNumbers = rules.Select((r, index) => (Rule: r, Number: (RuleNumber)index))
			.ToDictionary(t => t.Rule, t => t.Number);
		this._rulesByNumber = ruleNumbers.OrderBy(kvp => kvp.Value).Select(kvp => kvp.Key).ToArray();
		
		var ruleRemainderNumbers = rules.SelectMany(
				r => Enumerable.Range(0, r.Symbols.Length + 1),
				(r, i) => new RuleRemainder(r, i)
			)
			.Select((r, index) => (Rule: r, Number: (RuleRemainderNumber)index))
			.ToDictionary(t => t.Rule, t => t.Number);
		this._ruleRemaindersByNumber = ruleRemainderNumbers.OrderBy(kvp => kvp.Value).Select(kvp => kvp.Key).ToArray();
		
		var symbolNumbers = rules.SelectMany(s => s.Symbols.Append(s.Produced))
			.Distinct()
			.OrderByDescending(s => s is Token)
			.ThenBy(s => s.Name)
			.Select((s, index) => (Symbol: s, Number: (SymbolNumber)index))
			.ToDictionary(t => t.Symbol, t => t.Number);
		this._symbolsByNumber = symbolNumbers.OrderBy(kvp => kvp.Value).Select(kvp => kvp.Key).ToArray();
		this._isNonTerminalProcessed = new bool[symbolNumbers.Count];
			
		var rulesByProduced = rules.ToLookup(r => r.Produced);
		var firstNonTerminalsCache = new HashSet<SymbolNumber[]>(EqualityComparers.GetSequenceComparer<SymbolNumber>());
		this._lookaheadSymbolsByRuleRemainder = this._ruleRemaindersByNumber
			.Select(rr => firstFollow.NextOf(rr).Select(t => symbolNumbers[t]).OrderBy(t => t).ToArray())
			.ToArray();
		this._firstSymbolByRuleRemainder = this._ruleRemaindersByNumber
			.Select(rr => symbolNumbers[rr.Symbols[0]])
			.ToArray();

		this._positionState = Enumerable.Range(0, tokenCount)
			.Select(_ => new PositionState(symbolNumbers.Count, ruleRemainderNumbers.Count))
			.ToArray();
			
		SymbolNumber[] GetFirstNonTerminals(RuleRemainder ruleRemainder, Token lookahead)
		{
			if (ruleRemainder.Symbols.Length == 0 || ruleRemainder.Symbols[0] is Token) 
			{ 
				return Array.Empty<SymbolNumber>();
			}
			
			ImmutableHashSet<NonTerminal> GetFirstNonTerminalsHelper(NonTerminal nonTerminal, ImmutableHashSet<NonTerminal> visited)
			{	
				Debug.Assert(!visited.Contains(nonTerminal));
				visited = visited.Add(nonTerminal);
				
				var rules = rulesByProduced[nonTerminal].Select(r => new RuleRemainder(r, 0))
					.Where(rr => firstFollow.NextOf(rr).Contains(lookahead))
					.ToArray();
				if (rules.Any(r =>r .Symbols
					
					
					.Select(rr => !rr.Symbols.IsEmpty GetFirstNonTerminals(rr, lookahead))
					.Aggregate(ImmutableHashSet<NonTerminal>.Empty, (acc, f) => acc.Intersect(f.Select(s => (NonTerminal)this._symbolsByNumber[(int)s])))
				
			}
		}
	}
	
	public void Run()
	{
		this._positionState[0].Heads.AddUnchecked(
			(int)default(RuleRemainderNumber), 
			new GraphStructuredStackNode(default(RuleRemainderNumber), parent: null)
		);
		this.PopulateLookaheadSymbols(this._positionState[0].LookaheadSymbols, this._positionState[0].Heads);

		var position = 0;
		while (true)
		{
			ref var positionState = ref this._positionState[position];	
			
			if (positionState.LookaheadSymbols.TryPop(out var nextLookaheadSymbol))
			{
				++position;
			}
			else 
			{
				Debug.Assert(positionState.LookaheadSymbols.Elements.IsEmpty);
				positionState.Heads.Clear();
				positionState.CompletedLookaheadNonTerminals.Clear();
				--position;
			}
		}
	}
	
	private void PopulateLookaheadSymbols(SparseSet lookaheadSymbols, SparseDictionary<GraphStructuredStackNode> heads)		
	{
		for (var i = 0; i < heads.Count; ++i)
		{
			var head = (RuleRemainderNumber)heads.GetKey(i);
			
		}
	}

	enum SymbolNumber { Invalid = -1 }
	enum RuleNumber { }
	enum RuleRemainderNumber { }

	private struct PositionState
	{
		internal SparseSet LookaheadSymbols;
		internal SparseDictionary<GraphStructuredStackNode> Heads;
		internal SparseSet CompletedLookaheadNonTerminals;
		internal SymbolNumber LastLookaheadSymbol;

		public PositionState(int symbolCount, int ruleRemainderCount)
		{
			this.LookaheadSymbols = new(symbolCount);
			this.Heads = new(ruleRemainderCount);
			this.CompletedLookaheadNonTerminals = new(symbolCount);
			this.LastLookaheadSymbol = SymbolNumber.Invalid;
		}
	}

	sealed class GraphStructuredStackNode
	{		
		private object _previousNodes;
		
		public GraphStructuredStackNode(RuleRemainderNumber rule, GraphStructuredStackNode? parent)
		{
			this.Rule = rule;
			this._previousNodes = parent ?? (object)new List<GraphStructuredStackNode>();
		}
		
		public RuleRemainderNumber Rule { get; }

		public ReadOnlySpan<GraphStructuredStackNode> PreviousNodes
		{
			get 
			{
				return this._previousNodes is GraphStructuredStackNode
					? MemoryMarshal.CreateReadOnlySpan(ref Unsafe.As<object, GraphStructuredStackNode>(ref this._previousNodes), 1)
					: CollectionsMarshal.AsSpan(Unsafe.As<List<GraphStructuredStackNode>>(this._previousNodes));
			}
		}
		
		public void AddPrevious(GraphStructuredStackNode node)
		{
			Debug.Assert(!this.PreviousNodes.IsEmpty, "Should not add previous to root node");
			if (this._previousNodes is GraphStructuredStackNode previousNode)
			{
				this._previousNodes = new List<GraphStructuredStackNode>(capacity: 4) { previousNode, node };
			}
			else { Unsafe.As<List<GraphStructuredStackNode>>(this._previousNodes).Add(node); }
		}
	}
	
	private sealed class SparseSet
	{
		private readonly List<int> _list;
		private readonly int[] _sparse;
		
		public SparseSet(int maxCount)
		{
			this._list = new(capacity: maxCount);
			this._sparse = new int[maxCount];
		}
		
		public bool TryPop(out int removed)
		{
			var count = this._list.Count;
			if (count == 0) 
			{ 
				removed = -1; 
				return false; 
			}
			
			removed = this._list[count - 1];
			this._list.RemoveAt(count - 1);
			return true;
		}
		
		public ReadOnlySpan<int> Elements => CollectionsMarshal.AsSpan(this._list);
		
		public void Clear() => this._list.Clear();
		
		public bool Contains(int value)
		{
			var sparse = this._sparse[value];
			return sparse < this._list.Count && this._list[sparse] == value;
		}
		
		public void AddUnchecked(int value)
		{
			Debug.Assert(!this.Contains(value));
			this._sparse[value] = this._list.Count;
			this._list.Add(value);
		}
	}

	private sealed class SparseDictionary<TValue>
	{
		private readonly List<(int Key, TValue Value)> _list;
		private readonly int[] _sparse;

		public SparseDictionary(int maxCount)
		{
			this._list = new(capacity: maxCount);
			this._sparse = new int[maxCount];
		}
		
		public int Count => this._list.Count;
		
		public int GetKey(int index) => this._list[index].Key;
		
		public bool TryGetValue(int key, out TValue value)
		{
			var sparse = this._sparse[key];
			if (sparse < this._list.Count)
			{
				var entry = this._list[sparse];
				if (entry.Key == key)
				{
					value = entry.Value;
					return true;
				}
			}
			
			value = default!;
			return false;
		}
		
		public void Clear() => this._list.Clear();

		public bool ContainsKey(int key)
		{
			var sparse = this._sparse[key];
			return sparse < this._list.Count && this._list[sparse].Key == key;
		}

		public void AddUnchecked(int key, TValue value)
		{
			Debug.Assert(!this.ContainsKey(key));
			this._sparse[key] = this._list.Count;
			this._list.Add((key, value));
		}
	}
}

public record Context(string Name);

public abstract record Symbol(string Name);
public record Token(string Name) : Symbol(Name);
public record NonTerminal(string Name) : Symbol(Name);
public record Rule(NonTerminal Produced, params Symbol[] Symbols);
public record RuleRemainder(Rule Rule, int Position) { public ReadOnlySpan<Symbol> Symbols => this.Rule.Symbols.AsSpan(this.Position); }

#region FirstFollowProvider
internal class FirstFollowCalculator
{
	private readonly IReadOnlyDictionary<Symbol, ImmutableHashSet<Token?>> _firstSets;
	private readonly IReadOnlyDictionary<Symbol, ImmutableHashSet<Token>> _followSets;
	private readonly FirstFollowCalculator? _reverseCalculator;

	private FirstFollowCalculator(
		IReadOnlyDictionary<Symbol, ImmutableHashSet<Token?>> firstSets,
		IReadOnlyDictionary<Symbol, ImmutableHashSet<Token>> followSets,
		FirstFollowCalculator? reverseCalculator)
	{
		this._firstSets = firstSets;
		this._followSets = followSets;
		this._reverseCalculator = reverseCalculator;
	}

	public static FirstFollowCalculator Create(IReadOnlyCollection<Rule> rules, bool skipReverseCalculation = false)
	{
		var allSymbols = new HashSet<Symbol>(rules.SelectMany(r => r.Symbols).Concat(rules.Select(r => r.Produced)));
		var firstSets = ComputeFirstSets(allSymbols, rules);
		var followSets = ComputeFollowSets(allSymbols, rules, firstSets);
		var reverseCalculator = skipReverseCalculation
			? null
			: Create(rules.Select(r => new Rule(r.Produced, r.Symbols.Reverse().ToArray())).ToArray(), skipReverseCalculation: true);

		return new FirstFollowCalculator(firstSets, followSets, reverseCalculator);
	}

	private static IReadOnlyDictionary<Symbol, ImmutableHashSet<Token?>> ComputeFirstSets(
		IReadOnlyCollection<Symbol> allSymbols,
		IReadOnlyCollection<Rule> rules)
	{
		// initialize all first sets to empty for non-terminals and the terminal itself for terminals
		var firstSets = allSymbols.ToDictionary(
			s => s,
			s => s is Token t ? new HashSet<Token?> { t } : new HashSet<Token?>()
		);

		// iteratively build the first sets for the non-terminals
		bool changed;
		do
		{
			changed = false;
			foreach (var rule in rules)
			{
				// for each symbol, add first(symbol) - null to first(produced)
				// until we hit a non-nullable symbol
				var nullable = true;
				foreach (var symbol in rule.Symbols)
				{
					foreach (var token in firstSets[symbol])
					{
						if (token != null)
						{
							changed |= firstSets[rule.Produced].Add(token);
						}
					}
					if (!firstSets[symbol].Contains(null))
					{
						nullable = false;
						break;
					}
				}

				// if all symbols were nullable, then produced is nullable
				if (nullable)
				{
					changed |= firstSets[rule.Produced].Add(null);
				}
			}
		} while (changed);

		return firstSets.ToDictionary(kvp => kvp.Key, kvp => ImmutableHashSet.CreateRange(kvp.Value));
	}

	private static IReadOnlyDictionary<Symbol, ImmutableHashSet<Token>> ComputeFollowSets(
		IReadOnlyCollection<Symbol> allSymbols,
		IReadOnlyCollection<Rule> rules,
		IReadOnlyDictionary<Symbol, ImmutableHashSet<Token?>> firstSets)
	{
		// start everything with an empty follow set
		var followSets = allSymbols.ToDictionary(s => s, s => new HashSet<Token>());

		// now iteratively build up the follow sets

		// NOTE: this could be more efficient because everything relating to first sets won't do anything new after the first
		// pass. We could thus move to a two-pass approach where the first pass runs once and the second pass just propagates back
		// the produced symbol follow set

		bool changed;
		do
		{
			changed = false;
			foreach (var rule in rules)
			{
				for (var i = 0; i < rule.Symbols.Length; ++i)
				{
					var followSet = followSets[rule.Symbols[i]];
					var foundNonNullableFollowingSymbol = false;
					for (var j = i + 1; j < rule.Symbols.Length; ++j)
					{
						var followingFirstSet = firstSets[rule.Symbols[j]];

						// add all tokens in the first set of the following symbol j
						foreach (var token in followingFirstSet.Where(t => t != null))
						{
							changed |= followSet.Add(token!);
						}

						// if the symbol j is non-nullable, stop
						if (!followingFirstSet.Contains(null))
						{
							foundNonNullableFollowingSymbol = true;
							break;
						}
					}

					// if there are no non-nullable symbols between i and the end of the rule, then
					// we add the follow of the produced symbol to the follow of i
					if (!foundNonNullableFollowingSymbol && rule.Symbols[i] != rule.Produced)
					{
						foreach (var token in followSets[rule.Produced])
						{
							changed |= followSet.Add(token);
						}
					}
				}
			}
		} while (changed);

		return followSets.ToDictionary(kvp => kvp.Key, kvp => ImmutableHashSet.CreateRange(kvp.Value));
	}

	public ImmutableHashSet<Token?> FirstOf(Symbol symbol) => this._firstSets[symbol];
	public ImmutableHashSet<Token> FollowOf(Symbol symbol) => this._followSets[symbol];
	public ImmutableHashSet<Token> FollowOf(Rule rule) => this.FollowOf(rule.Produced);
	public ImmutableHashSet<Token?> LastOf(Symbol symbol) => this._reverseCalculator!.FirstOf(symbol);

	public ImmutableHashSet<Token> NextOf(RuleRemainder ruleRemainder)
	{
		var firsts = this.FirstOf(ruleRemainder.Symbols);
#nullable disable
		return firsts.Contains(null)
			? firsts.Remove(null).Union(this.FollowOf(ruleRemainder.Rule))
			: firsts;
#nullable enable
	}

	public ImmutableHashSet<Token?> FirstOf(ReadOnlySpan<Symbol> symbols)
	{
		var builder = ImmutableHashSet.CreateBuilder<Token?>();
		foreach (var symbol in symbols)
		{
			var firsts = this.FirstOf(symbol);
			builder.UnionWith(firsts.Where(s => s != null));
			if (!firsts.Contains(null))
			{
				// not nullable
				return builder.ToImmutable();
			}
		}

		// if we reach here, we're nullable
		builder.Add(null);
		return builder.ToImmutable();
	}
}
#endregion