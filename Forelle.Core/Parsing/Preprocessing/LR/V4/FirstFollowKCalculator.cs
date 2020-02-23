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

        // todo trie
        private IReadOnlyDictionary<Symbol, ImmutableHashSet<IReadOnlyList<Token>>> ComputeFirstSets(int k)
        {
            Invariant.Require(k >= 0);

            var allSymbols = this._rulesByProduced.SelectMany(g => g.SelectMany(r => r.Symbols).Concat(new[] { g.Key }))
                .Distinct()
                .ToArray();

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
                                    var prefixesAndChildIncompletes = childIncomplete.ToArray() // avoid concurrent modificiation
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
                                var prefixesAndChildCompletes = completed[childSymbol].ToArray() // avoid concurrent modification
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
    }
}
