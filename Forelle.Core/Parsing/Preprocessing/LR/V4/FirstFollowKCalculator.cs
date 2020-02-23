using Medallion.Collections;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;

namespace Forelle.Parsing.Preprocessing.LR.V3
{
    internal class FirstFollowKCalculator
    {
        private static readonly IEqualityComparer<IReadOnlyList<Token>> Comparer = EqualityComparers.GetSequenceComparer<Token>();

        private readonly ILookup<NonTerminal, Rule> _rulesByProduced;
        private readonly Dictionary<int, IReadOnlyDictionary<Symbol, ImmutableHashSet<IReadOnlyList<Token>>>> _firstSetsByK = 
            new Dictionary<int, IReadOnlyDictionary<Symbol, ImmutableHashSet<IReadOnlyList<Token>>>>();
        private readonly Dictionary<int, IReadOnlyDictionary<Symbol, ImmutableHashSet<IReadOnlyList<Token>>>> _followSetsByK =
            new Dictionary<int, IReadOnlyDictionary<Symbol, ImmutableHashSet<IReadOnlyList<Token>>>>();

        public FirstFollowKCalculator(ILookup<NonTerminal, Rule> rulesByProduced)
        {
            this._rulesByProduced = rulesByProduced;
        }

        public ImmutableHashSet<IReadOnlyList<Token>> FirstOf(Symbol symbol, int k)
        {
            if (!this._firstSetsByK.TryGetValue(k, out var firstSets))
            {
                firstSets = this._firstSetsByK[k] = this.ComputeFirstSets(k);
            }

            return firstSets[symbol];
        }

        // todo can optimize
        public ImmutableHashSet<IReadOnlyList<Token>> FirstOf(IEnumerable<Symbol> symbols, int k)
        {
            var result = ImmutableHashSet.CreateBuilder(Comparer);
            HashSet<IReadOnlyList<Token>> incomplete = null;
            foreach (var symbol in symbols)
            {
                HashSet<IReadOnlyList<Token>> newIncomplete = null;

                var firstSet = this.FirstOf(symbol, k);
                foreach (var sequence in firstSet)
                {
                    if (incomplete == null) // first symbol
                    {
                        if (sequence.Count == k)
                        {
                            result.Add(sequence);
                        }
                        else
                        {
                            (newIncomplete ??= new HashSet<IReadOnlyList<Token>>(Comparer)).Add(sequence);
                        }
                    }
                    else
                    {
                        foreach (var incompleteSequence in incomplete)
                        {
                            // todo perf opt: fast concat of (a, b, k) here and elsewhere
                            var concatenated = incompleteSequence.Concat(sequence).Take(k).ToArray();
                            if (concatenated.Length == k)
                            {
                                result.Add(concatenated);
                            }
                            else
                            {
                                (newIncomplete ??= new HashSet<IReadOnlyList<Token>>(Comparer)).Add(concatenated);
                            }
                        }
                    }
                }

                incomplete = newIncomplete;
                if (incomplete == null) { break; }
            }

            // if the last symbol left incomplete sequences, then that means we can finish the symbols
            // with fewer than k tokens. Just add those incomplete sequences to the result
            if (incomplete != null)
            {
                result.UnionWith(incomplete);
            }

            return result.ToImmutable();
        }

        public ImmutableHashSet<IReadOnlyList<Token>> FollowOf(Symbol symbol, int k)
        {
            if (!this._followSetsByK.TryGetValue(k, out var followSets))
            {
                followSets = this._followSetsByK[k] = this.ComputeFollowSets(k);
            }

            return followSets[symbol];
        }

        public ImmutableHashSet<IReadOnlyList<Token>> NextOf(IEnumerable<Symbol> symbols, NonTerminal produced, int k)
        {
            var firstSet = this.FirstOf(symbols, k);
            var firstSetByIsKLength = firstSet.ToLookup(s => s.Count == k);

            if (!firstSetByIsKLength[false].Any()) { return firstSet; }

            var followSet = this.FollowOf(produced, k);

            return firstSetByIsKLength[false]
                .SelectMany(_ => followSet, (prefix, suffix) => prefix.Concat(suffix).Take(k).ToArray())
                .Concat(firstSetByIsKLength[true])
                .ToImmutableHashSet(Comparer);
        }

        // todo trie
        private IReadOnlyDictionary<Symbol, ImmutableHashSet<IReadOnlyList<Token>>> ComputeFirstSets(int k)
        {
            Invariant.Require(k >= 0);

            var allSymbols = this.GetAllSymbols().ToArray();

            if (k == 0)
            {
                return allSymbols.ToDictionary(s => s, _ => ImmutableHashSet.Create(Comparer, Array.Empty<Token>())); // todo could cache
            }

            var completed = allSymbols.ToDictionary(s => s, _ => new HashSet<IReadOnlyList<Token>>(Comparer)); // todo could cache

            // pre-compute all token results
            foreach (var token in allSymbols.OfType<Token>())
            {
                var tokenFirst = new Token[Math.Min(k, 2)];
                tokenFirst[0] = token;
                completed[token].Add(tokenFirst);
            }

            // will store lists of tokens with length < k and no null terminator that are 
            // the best we've been able to compute so far. These may in turn be used to 
            // derive completed lists for other symbols
            var incomplete = allSymbols.OfType<NonTerminal>()
                // See everything with an initial incomplete set of just the empty sequence. This ensures that
                // we don't improperly wipe out incomplete sets in the first pass
                .ToDictionary(s => s, _ => ImmutableHashSet.Create(Comparer, Array.Empty<Token>())); // todo could cache

            bool changed;
            do
            {
                changed = false;

                // iterate over a copy of incomplete, since we'll modify it as we go
                foreach (var (nonTerminal, nonTerminalIncomplete) in incomplete.ToArray())
                {
                    var newNonTerminalIncomplete = ImmutableHashSet.CreateBuilder(Comparer);

                    foreach (var rule in this._rulesByProduced[nonTerminal])
                    {
                        if (rule.Symbols.Count == 0)
                        {
                            // todo could cache
                            changed |= completed[nonTerminal].Add(new Token[] { null });
                        }
                        else
                        {
                            // todo could cache
                            IReadOnlyList<Token[]> prefixes = new[] { Array.Empty<Token>() };
                            for (var i = 0; i < rule.Symbols.Count; ++i)
                            {
                                var childSymbol = rule.Symbols[i];

                                // If the next child is a non terminal with incomplete sequences, then consider
                                // the cross product of prefixes and those sequences. This can feed into incomplete
                                // or completed, but cannot feed back into the next round of prefixes
                                if (childSymbol is NonTerminal childNonTerminal
                                    && incomplete.TryGetValue(childNonTerminal, out var childIncomplete))
                                {
                                    var prefixesAndChildIncompletes = childIncomplete.ToArray() // allow concurrent modificiation
                                        .SelectMany(_ => prefixes, (childIncompleteSequence, prefix) => prefix.Concat(childIncompleteSequence))
                                        .Select(s => s.Take(k).ToArray());
                                    foreach (var prefixThenChildIncomplete in prefixesAndChildIncompletes)
                                    {
                                        if (prefixThenChildIncomplete.Length == k)
                                        {
                                            changed |= completed[nonTerminal].Add(prefixThenChildIncomplete);
                                        }
                                        else
                                        {
                                            newNonTerminalIncomplete.Add(prefixThenChildIncomplete);
                                        }
                                    }
                                }

                                // Next, compute the cross-product of prefixes with completeds for the child symbol
                                var prefixesAndChildCompletes = completed[childSymbol].ToArray() // allow concurrent modification
                                    .SelectMany(_ => prefixes, (childCompleteSequence, prefix) => prefix.Concat(childCompleteSequence))
                                    .Select(s => s.Take(k).ToArray());
                                List<Token[]> newPrefixes = null;
                                foreach (var prefixThenChildCompleteSequence in prefixesAndChildCompletes)
                                {
                                    // ending in null means that the child symbol completed without a full sequence of k
                                    if (prefixThenChildCompleteSequence[prefixThenChildCompleteSequence.Length - 1] == null)
                                    {
                                        // if this is the final child, then that means that the current symbol can also complete
                                        // without a full k tokens. Just add to completed
                                        if (i == rule.Symbols.Count - 1)
                                        {
                                            changed |= completed[nonTerminal].Add(prefixThenChildCompleteSequence);
                                        }
                                        else
                                        {
                                            // otherwise, this sequence (minus the trailing null) will form a prefix going
                                            // into child symbol i+1
                                            (newPrefixes ??= new List<Token[]>())
                                                .Add(prefixThenChildCompleteSequence.Take(prefixThenChildCompleteSequence.Length - 1).ToArray());
                                        }
                                    }
                                    else
                                    {
                                        // otherwise, we must now have a full sequence of k tokens for the current symbol!
                                        Invariant.Require(prefixThenChildCompleteSequence.Length == k);
                                        changed |= completed[nonTerminal].Add(prefixThenChildCompleteSequence);
                                    }
                                }

                                if (newPrefixes == null) { break; }
                                prefixes = newPrefixes;
                            }
                        }
                    }

                    if (newNonTerminalIncomplete.Count == 0)
                    {
                        incomplete.Remove(nonTerminal); // all done!
                    }
                    else
                    {
                        // Otherwise, we update the incomplete set. If this is a change, then this can count
                        // as forward progress. However, we avoid diffing the sets unless we haven't already
                        // registered a different change to avoid the expense

                        if (!changed)
                        {
                            if (!newNonTerminalIncomplete.SetEquals(nonTerminalIncomplete))
                            {
                                incomplete[nonTerminal] = newNonTerminalIncomplete.ToImmutable();
                                changed = true;
                            }
                        }
                        else
                        {
                            incomplete[nonTerminal] = newNonTerminalIncomplete.ToImmutable();
                        }
                    }
                }
            } while (changed);

            return completed.ToDictionary(
                kvp => kvp.Key,
                kvp => kvp.Value.Select(s => s[s.Count - 1] == null ? s.Take(s.Count - 1).ToArray() : s)
                    .ToImmutableHashSet(Comparer)
            );
        }

        private IReadOnlyDictionary<Symbol, ImmutableHashSet<IReadOnlyList<Token>>> ComputeFollowSets(int k)
        {
            Invariant.Require(k >= 0);

            var completed = this.GetAllSymbols()
                .ToDictionary(s => s, s => ImmutableHashSet.CreateBuilder(Comparer));
            var incomplete = completed.Keys
                .ToDictionary(s => s, s => ImmutableHashSet.Create(Comparer));

            // first, pre-compute the first set for each sequence of symbols REM following S in a rule P -> ... S REM
            var symbolsToRemaindersAndFirstSets = this._rulesByProduced.SelectMany(g => g)
                .SelectMany(r => Enumerable.Range(0, r.Symbols.Count), (r, index) => (symbol: r.Symbols[index], remainder: r.Skip(index + 1)))
                .ToLookup(
                    t => t.symbol,
                    t => (t.remainder, firstSet: this.FirstOf(t.remainder.Symbols, k))
                );

            // Symbols which are never referenced cannot be followed, while symbols which are only referenced
            // recursively might be follow-able but also can appear at the end of strings in the language.
            // For these symbols, pre-populate the completed set with the empty sequence
            foreach (var symbol in completed.Keys
                .Where(s => !symbolsToRemaindersAndFirstSets[s].Any() || symbolsToRemaindersAndFirstSets[s].All(t => t.remainder.Produced == s)))
            {
                completed[symbol].Add(Array.Empty<Token>());
            }

            bool changed;
            do
            {
                changed = false;

                foreach (var (symbol, symbolIncomplete) in incomplete.ToArray()) // allow concurrent modification
                {
                    var newSymbolIncomplete = ImmutableHashSet.CreateBuilder(Comparer);

                    foreach (var (remainder, firstSet) in symbolsToRemaindersAndFirstSets[symbol])
                    {
                        foreach (var firstSetSequence in firstSet)
                        {
                            if (firstSetSequence.Count == k)
                            {
                                changed |= completed[symbol].Add(firstSetSequence);
                            }
                            else
                            {
                                // try combining with each incomplete sequence
                                foreach (var incompleteSequence in incomplete[remainder.Produced])
                                {
                                    var concatenated = firstSetSequence.Concat(incompleteSequence).Take(k).ToArray();
                                    if (concatenated.Length == k)
                                    {
                                        changed |= completed[symbol].Add(concatenated);
                                    }
                                    else
                                    {
                                        newSymbolIncomplete.Add(concatenated);
                                    }
                                }

                                // combine with each complete sequence
                                foreach (var completedSequence in completed[remainder.Produced].ToArray()) // avoid concurrent modification
                                {
                                    var concatenated = firstSetSequence.Concat(completedSequence).Take(k).ToArray();
                                    changed |= completed[symbol].Add(concatenated);
                                }
                            }
                        }
                    }

                    if (newSymbolIncomplete.Count == 0)
                    {
                        incomplete.Remove(symbol); // all done
                    }
                    else
                    {
                        // see similar logic in ComputeFirstSets
                        if (!changed)
                        {
                            if (!newSymbolIncomplete.SetEquals(symbolIncomplete))
                            {
                                incomplete[symbol] = newSymbolIncomplete.ToImmutable();
                                changed = true; // forward progress made!
                            }
                        }
                        else
                        {
                            incomplete[symbol] = newSymbolIncomplete.ToImmutable();
                        }
                    }
                }
            }
            while (changed);

            return completed.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.ToImmutable());
        }

        private IEnumerable<Symbol> GetAllSymbols() => this._rulesByProduced
            .SelectMany(g => g.SelectMany(r => r.Symbols).Concat(new[] { g.Key }))
            .Distinct();
    }
}
