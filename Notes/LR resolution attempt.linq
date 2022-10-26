<Query Kind="Program">
  <NuGetReference>MedallionComparers</NuGetReference>
  <Namespace>System.Collections.Immutable</Namespace>
  <Namespace>System.Runtime.CompilerServices</Namespace>
  <Namespace>Medallion.Collections</Namespace>
</Query>

#nullable enable

void Main()
{
	//TestSimpleAmbiguity();	
	TestLargeGrammar();
}

class LRGenerator
{
	private readonly Rule _start;
	private readonly FirstFollowCalculator _firstFollow;
	private readonly ILookup<NonTerminal, Rule> _rulesByProduced;
	private readonly Dictionary<IReadOnlyCollection<LRItem>, HashSet<LRItem>> _statesByKernel = new(EqualityComparers.GetCollectionComparer<LRItem>());
	private readonly HashSet<HashSet<LRItem>> _states = new(EqualityComparers.GetCollectionComparer<LRItem>());
	private readonly Dictionary<HashSet<LRItem>, HashSet<(Symbol Symbol, HashSet<LRItem> Destination)>> _edges = new();
	private readonly Dictionary<HashSet<LRItem>, HashSet<(Token Token, Rule Rule)>> _reductions = new();

	private readonly Dictionary<HashSet<LRItem>, HashSet<(Token Token, Dictionary<HashSet<LRItem>, HashSet<LRItem>> Mapping)>> _mappedShiftReductions = new();

	public LRGenerator(Rule[] rules)
	{
		this._start = rules[0];
		this._rulesByProduced = rules.ToLookup(r => r.Produced);
		this._firstFollow = FirstFollowCalculator.Create(rules);
	}

	public void Generate()
	{
		this.Closure(new[] { new LRItem(new(this._start, 0), null!) }, out var startState);
		var statesToProcess = new Queue<HashSet<LRItem>>();
		statesToProcess.Enqueue(startState);

		void ProcessStates()
		{
			while (statesToProcess.TryDequeue(out var state))
			{
				foreach (var item in state)
				{
					if (item.Rule.Symbols.IsEmpty)
					{
						if (!this._reductions.TryGetValue(state, out var reductions))
						{
							this._reductions.Add(state, reductions = new());
						}
						reductions.Add((item.Lookahead, item.Rule.Rule));
					}
					else if (this.Goto(state, item.Rule.Symbols[0], out var newState))
					{
						statesToProcess.Enqueue(newState);
					}
				}
			}
		}
		ProcessStates();

		var table = this._edges.SelectMany(
			kvp => kvp.Value.Select(t => new { State = kvp.Key, Symbol = t.Symbol, Type = "Shift", Target = (object)t.Destination }))
			.Concat(this._reductions.SelectMany(
				kvp => kvp.Value.Select(t => new { State = kvp.Key, Symbol = (Symbol)t.Token, Type = "Reduce", Target = (object)t.Rule })))
			.Concat(this._mappedShiftReductions.SelectMany(
				kvp => kvp.Value.Select(t => new { State = kvp.Key, Symbol = (Symbol)t.Token, Type = "MappedShiftReduce", Target = (object)t.Mapping })))
			.ToLookup(t => new { t.State, t.Symbol }, t => new { t.Type, t.Target });
		var conflicts = table.Where(g => g.Count() > 1).ToArray();	
		new
		{
			States = table.Count,
			Conflicts = conflicts.Select(c => new
			{
				State = ToString(c.Key.State),
				Symbol = c.Key.Symbol.ToString(),
				Actions = c.Select(a => new { a.Type, Target = a.Target is Rule rule ? new RuleRemainder(rule, rule.Symbols.Length).ToString() : a.Target.GetHashCode().ToString() }).ToArray()
			})
		}.Dump("pre-resolution conflicts");

		foreach (var conflictToResolve in conflicts)
		{
			if (conflictToResolve.All(a => a.Type == "Reduce"))
			{
				var reductionTargetStates = GetReductionTargetStates(conflictToResolve.Key.State, (Rule)conflictToResolve.First().Target);
				var rts2 = GetReductionTargetStates(conflictToResolve.Key.State, (Rule)conflictToResolve.Last().Target);
				if (!EqualityComparers.GetCollectionComparer<HashSet<LRItem>>().Equals(reductionTargetStates, rts2)) { throw new Exception(); }
				var rules = conflictToResolve.Select(r => (Rule)r.Target).ToArray();
				var stateMapping = new Dictionary<HashSet<LRItem>, HashSet<LRItem>>();
				foreach (var reductionTargetState in reductionTargetStates)
				{
					var originalGotoStates = this._edges[reductionTargetState]
						.Where(e => rules.Any(r => r.Produced == e.Symbol))
						.Select(e => e.Destination);
					var newGotoKernel = originalGotoStates.Aggregate<IEnumerable<LRItem>>((s1, s2) => s1.Union(s2))
						.Select(i => (Item: i, Rule: rules.SingleOrDefault(r => r.Produced == (i.Rule.Position < 1 ? null : i.Rule.Rule.Symbols[i.Rule.Position - 1]))))
						.Select(
							t => t.Rule is null
								? t.Item
								: new LRItem(
									new RuleRemainder(
										new(
											t.Item.Rule.Rule.Produced,
											t.Item.Rule.Rule.Symbols.Take(t.Item.Rule.Position - 1)
												.Concat(t.Rule.Symbols)
												.Concat(t.Item.Rule.Rule.Symbols.Skip(t.Item.Rule.Position))
												.ToArray()
										),
										t.Item.Rule.Position + t.Rule.Symbols.Length - 1
									),
									t.Item.Lookahead
								)
						)
						.ToArray();
					if (this.Closure(newGotoKernel, out var newGotoState))
					{
						statesToProcess.Enqueue(newGotoState);
					}
					stateMapping.Add(reductionTargetState, newGotoState);
				}
				this._reductions[conflictToResolve.Key.State].RemoveWhere(r => r.Token == conflictToResolve.Key.Symbol);
				if (!this._mappedShiftReductions.TryGetValue(conflictToResolve.Key.State, out var mappedShiftReductions))
				{
					this._mappedShiftReductions.Add(conflictToResolve.Key.State, mappedShiftReductions = new());
				}
				mappedShiftReductions.Add(((Token)conflictToResolve.Key.Symbol, stateMapping));
			}
			ProcessStates();
		}

		table = this._edges.SelectMany(
			kvp => kvp.Value.Select(t => new { State = kvp.Key, Symbol = t.Symbol, Type = "Shift", Target = (object)t.Destination }))
			.Concat(this._reductions.SelectMany(
				kvp => kvp.Value.Select(t => new { State = kvp.Key, Symbol = (Symbol)t.Token, Type = "Reduce", Target = (object)t.Rule })))
			.Concat(this._mappedShiftReductions.SelectMany(
				kvp => kvp.Value.Select(t => new { State = kvp.Key, Symbol = (Symbol)t.Token, Type = "MappedShiftReduce", Target = (object)t.Mapping })))
			.ToLookup(t => new { t.State, t.Symbol }, t => new { t.Type, t.Target });
		conflicts = table.Where(g => g.Count() > 1).ToArray();
		new
		{
			States = table.Count,
			Conflicts = conflicts.Select(c => new
			{
				State = ToString(c.Key.State),
				Symbol = c.Key.Symbol.ToString(),
				Actions = c.Select(a => new { a.Type, Target = a.Target is Rule rule ? new RuleRemainder(rule, rule.Symbols.Length).ToString() : a.Target.GetHashCode().ToString() }).ToArray()
			})
		}.Dump("post-resolution conflicts");

		string ToString(IEnumerable<LRItem> state) => string.Join(Environment.NewLine, state.Select(i => i.ToString()).OrderBy(s => s));
	}

	private IReadOnlyCollection<HashSet<LRItem>> GetReductionTargetStates(HashSet<LRItem> state, Rule rule)
	{
		var result = new HashSet<HashSet<LRItem>>();
		GatherReductionTargetStates(state, new RuleRemainder(rule, rule.Symbols.Length));
		return result;
		
		void GatherReductionTargetStates(HashSet<LRItem> state, RuleRemainder ruleRemainder)
		{
			if (ruleRemainder.Position == 0)
			{
				result.Add(state);
				return;
			}
			
			var previousRuleRemainder = new RuleRemainder(ruleRemainder.Rule, ruleRemainder.Position - 1);
			var shiftedSymbol = previousRuleRemainder.Symbols[^1];
			var fromStates = this._edges.SelectMany(kvp => kvp.Value, (kvp, e) => (State: kvp.Key, e.Symbol, e.Destination))
				.Where(t => t.Destination == state && t.Symbol == shiftedSymbol)
				.Select(t => t.State);
			foreach (var fromState in fromStates)
			{
				GatherReductionTargetStates(fromState, previousRuleRemainder);
			}
		}
	}

	private bool Closure(IReadOnlyCollection<LRItem> kernel, out HashSet<LRItem> state)
	{
		Debug.Assert(kernel.Count > 0);

		if (this._statesByKernel.TryGetValue(kernel, out var existing))
		{
			state = existing;
			return false;
		}

		var items = new Queue<LRItem>(kernel);
		var result = new HashSet<LRItem>();
		while (items.TryDequeue(out var item))
		{
			if (result.Add(item)
				&& !item.Rule.Symbols.IsEmpty
				&& item.Rule.Symbols[0] is NonTerminal nonTerminal)
			{
				var lookahead = this._firstFollow.FirstOf(item.Rule.Symbols.Slice(1));
				if (lookahead.Contains(null)) { lookahead = lookahead.Remove(null).Add(item.Lookahead); }

				foreach (var rule in this._rulesByProduced[nonTerminal])
				{
					foreach (var token in lookahead)
					{
						var newItem = new LRItem(new(rule, 0), token!);
						items.Enqueue(newItem);
					}
				}
			}
		}

		if (!this._states.Add(result))
		{
			state = this._states.TryGetValue(result, out existing) ? existing : throw new Exception();
			this._statesByKernel.Add(kernel, existing);
			return false;
		}

		this._statesByKernel.Add(kernel, result);
		state = result;
		return true;
	}

	private bool Goto(HashSet<LRItem> state, Symbol symbol, out HashSet<LRItem> newState)
	{
		var newKernel = new List<LRItem>();
		foreach (var item in state)
		{
			if (!item.Rule.Symbols.IsEmpty && item.Rule.Symbols[0] == symbol)
			{
				newKernel.Add(new(new(item.Rule.Rule, item.Rule.Position + 1), item.Lookahead));
			}
		}

		var result = this.Closure(newKernel, out newState);
		if (!this._edges.TryGetValue(state, out var edges))
		{
			this._edges.Add(state, edges = new());
		}
		edges.Add((symbol, newState));
		return result;
	}
}

public class LRState
{
	public static readonly IEqualityComparer<LRItem[]> KernelComparer =
		EqualityComparers.GetCollectionComparer<LRItem>();

	public LRItem[] Kernel = default!;
	public HashSet<LRItem> Items = default!;

	public override bool Equals(object? obj) => obj is LRState that && KernelComparer.Equals(this.Kernel, that.Kernel);

	public override int GetHashCode() => KernelComparer.GetHashCode(this.Kernel);
}

public record LRItem(RuleRemainder Rule, Token Lookahead) { public override string ToString() => $"{this.Rule}, {this.Lookahead}"; }

public abstract record Symbol(string Name);
public record Token(string Name) : Symbol(Name) { public override string ToString() => this.Name; }
public record NonTerminal(string Name) : Symbol(Name) { public override string ToString() => this.Name; }
public record Rule(NonTerminal Produced, params Symbol[] Symbols);
public record RuleRemainder(Rule Rule, int Position)
{
	public ReadOnlySpan<Symbol> Symbols => this.Rule.Symbols.AsSpan(this.Position);
	public override string ToString()
	{
		var builder = new StringBuilder($"{this.Rule.Produced} -> ");
		for (var i = 0; i <= this.Rule.Symbols.Length; ++i)
		{
			if (this.Position == i)
			{
				builder.Append(i > 0 ? " •" : "•");
				if (i < this.Rule.Symbols.Length) { builder.Append(' '); }
			}
			if (i < this.Rule.Symbols.Length)
			{
				if (i > 0) { builder.Append(' '); }
				builder.Append(this.Rule.Symbols[i]);
			}
		}
		return builder.ToString();
	}
}

#region FirstFollowProvider
internal class FirstFollowCalculator
{
	private readonly IReadOnlyDictionary<Symbol, ImmutableHashSet<Token?>> _firstSets;
	private readonly IReadOnlyDictionary<Symbol, ImmutableHashSet<Token>> _followSets;

	private FirstFollowCalculator(
		IReadOnlyDictionary<Symbol, ImmutableHashSet<Token?>> firstSets,
		IReadOnlyDictionary<Symbol, ImmutableHashSet<Token>> followSets)
	{
		this._firstSets = firstSets;
		this._followSets = followSets;
	}

	public static FirstFollowCalculator Create(IReadOnlyCollection<Rule> rules)
	{
		var allSymbols = new HashSet<Symbol>(rules.SelectMany(r => r.Symbols).Concat(rules.Select(r => r.Produced)));
		var firstSets = ComputeFirstSets(allSymbols, rules);
		var followSets = ComputeFollowSets(allSymbols, rules, firstSets);

		return new FirstFollowCalculator(firstSets, followSets);
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

	public ImmutableHashSet<Token?> NextOf(RuleRemainder ruleRemainder)
	{
		var firsts = this.FirstOf(ruleRemainder.Symbols);
		return firsts.Contains(null)
			? firsts.Remove(null).Union(this.FollowOf(ruleRemainder.Rule))
			: firsts;
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

#region Test Cases
NonTerminal Start = new("Start"), Exp = new("Exp"), Stmt = new("Stmt");
Token eof = new("EOF"), dot = new("DOT"), semicolon = new("SEMICOLON"), id = new("ID"), num = new("NUM"),
	leftParen = new("LEFTPAREN"), rightParen = new("RIGHTPAREN"), plus = new("PLUS"), minus = new("MINUS"),
	times = new("TIMES"), divide = new("DIVIDE"), comma = new("COMMA"), colon = new("COLON");

void TestSimpleAmbiguity()
{
	NonTerminal Foo = new("Foo"), Bar = new("Bar");
	var rules = new Rule[]
	{
		new(Start, Stmt, eof),
		new(Exp, Foo),
		new(Exp, Bar),
		new(Stmt, Foo, dot),
		new(Stmt, Bar, semicolon),
		new(Foo, id),
		new(Bar, id),
	};
	new LRGenerator(rules).Generate();
}

void TestLargeGrammar()
{
	Token goesTo = new("GOESTO"), var = new("VAR"), assign = new("ASSIGN"), @return = new("RETURN");
	NonTerminal Ident = new("Ident"), Tuple = new("Tuple"), TupleMemberBinding = new("TupleMemberBinding"),
		TupleMemberBindingList = new("List<TupleMemberBinding>"), ExpBlock = new("ExpBlock"), Lambda = new("Lambda"),
		LambdaParameters = new("LambdaArgs"), LambdaParametersList = new("List<LambdaArg>"),
		Assignment = new("Assignment"), Call = new("Call"), ArgList = new("List<Arg>"), StmtList = new("List<Statement>");

	var rules = new Rule[]
	{
		new(Start, StmtList, eof),

		new(StmtList, Stmt, StmtList),
		new(StmtList),

		new(Stmt, Exp, semicolon),
		new(Stmt, @return, Exp, semicolon),
		new(Stmt, Assignment),

		new(Exp, Ident),
		new(Exp, num),
		new(Exp, leftParen, Exp, rightParen),
		// associativity ambiguity
		//new(Exp, Exp, times, Exp),
		//new(Exp, Exp, plus, Exp),
		new(Exp, Tuple),
		new(Exp, ExpBlock),
		new(Exp, Lambda),
		new(Exp, Call),

		new(Ident, id),
		new(Ident, var),

		new(Tuple, leftParen, TupleMemberBindingList, rightParen),

		new(TupleMemberBindingList, TupleMemberBinding, comma, TupleMemberBindingList),
		new(TupleMemberBindingList, TupleMemberBinding),
		new(TupleMemberBindingList),

		new(TupleMemberBinding, Ident, colon, Exp),

		new(ExpBlock, leftParen, Stmt, StmtList, rightParen),

		new(Lambda, LambdaParameters, goesTo, Exp),

		new(LambdaParameters, Ident),
		new(LambdaParameters, leftParen, LambdaParametersList, rightParen),

		new(LambdaParametersList),
		new(LambdaParametersList, Ident, comma, LambdaParametersList),
		new(LambdaParametersList, Ident),

		new(Assignment, var, Ident, assign, Exp, semicolon),

		// precedence ambiguity with lambda: a => b(2) could be (a => b)(2)
		//new(Call, Exp, leftParen, ArgList, rightParen),

		new(ArgList),
		new(ArgList, Exp, comma, ArgList),
		new(ArgList, Exp),
	};

	new LRGenerator(rules).Generate();
}
#endregion