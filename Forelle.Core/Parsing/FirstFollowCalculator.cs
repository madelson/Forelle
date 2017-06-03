using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;

namespace Forelle.Parsing
{
    internal class FirstFollowCalculator : IFirstFollowProvider
    {
        private readonly IReadOnlyDictionary<Symbol, ImmutableHashSet<Token>> _firstSets, _followSets;

        private FirstFollowCalculator(
            IReadOnlyDictionary<Symbol, ImmutableHashSet<Token>> firstSets,
            IReadOnlyDictionary<Symbol, ImmutableHashSet<Token>> followSets)
        {
            this._firstSets = firstSets;
            this._followSets = followSets;
        }

        public static FirstFollowCalculator Create(IReadOnlyList<Rule> rules)
        {
            var allSymbols = new HashSet<Symbol>(rules.SelectMany(r => r.Symbols).Concat(rules.Select(r => r.Produced)));
            var firstSets = ComputeFirstSets(allSymbols, rules);
            var followSets = ComputeFollowSets(allSymbols, rules, firstSets);

            return new FirstFollowCalculator(firstSets, followSets);
        }

        private static IReadOnlyDictionary<Symbol, ImmutableHashSet<Token>> ComputeFirstSets(
            IReadOnlyCollection<Symbol> allSymbols,
            IReadOnlyCollection<Rule> rules)
        {
            // initialize all first sets to empty for non-terminals and the terminal itself for terminals
            var firstSets = allSymbols.ToDictionary(
                s => s,
                s => s is Token t ? new HashSet<Token> { t } : new HashSet<Token>()
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
            IReadOnlyDictionary<Symbol, ImmutableHashSet<Token>> firstSets)
        {
            // start everything with an empty follow set
            var followSets = allSymbols.ToDictionary(s => s, s => new HashSet<Token>());

            // for each start symbol, add the corresponding final token as the follow set
            foreach (var rule in rules)
            {
                if (rule.Produced.SyntheticInfo is StartSymbolInfo startInfo)
                {
                    // a start rule looks like Start<T> -> T End<T>, so we will add End<T> to Follow(T)
                    followSets[startInfo.Symbol].Add((Token)rule.Symbols.Last());
                }
            }
            
            // now iteratively build up the remaining follow sets
            bool changed;
            do
            {
                changed = false;
                foreach (var rule in rules)
                {
                    // going backwards reduces the iterations because we learn from the next symbol
                    for (var i = rule.Symbols.Count - 1; i >= 0; --i)
                    {
                        // for the last symbol, give it the follow of the produced symbol
                        if (i == rule.Symbols.Count - 1)
                        {
                            foreach (var token in followSets[rule.Produced])
                            {
                                changed |= followSets[rule.Symbols[i]].Add(token);
                            }
                        }
                        else
                        {
                            foreach (var token in firstSets[rule.Symbols[i + 1]])
                            {
                                if (token != null)
                                {
                                    // add the firsts of the next symbol
                                    changed |= followSets[rule.Symbols[i]].Add(token);
                                }
                                else
                                {
                                    // if the next symbol is nullable, also add its follows
                                    foreach (var followToken in followSets[rule.Symbols[i + 1]])
                                    {
                                        changed |= followSets[rule.Symbols[i]].Add(followToken);
                                    }
                                }
                            }
                        }
                    }
                }
            } while (changed);

            return followSets.ToDictionary(kvp => kvp.Key, kvp => ImmutableHashSet.CreateRange(kvp.Value));
        }

        public ImmutableHashSet<Token> FirstOf(Symbol symbol) => this._firstSets[symbol];
        public ImmutableHashSet<Token> FollowOf(Symbol symbol) => this._followSets[symbol];
        public ImmutableHashSet<Token> FollowOf(Rule rule) => this.FollowOf(rule.Produced);
    }
}
