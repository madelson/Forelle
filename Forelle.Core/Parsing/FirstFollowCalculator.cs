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
                    for (var i = 0; i < rule.Symbols.Count; ++i)
                    {
                        var followSet = followSets[rule.Symbols[i]];
                        var foundNonNullableFollowingSymbol = false;
                        for (var j = i + 1; j < rule.Symbols.Count; ++j)
                        {
                            var followingFirstSet = firstSets[rule.Symbols[j]];

                            // add all tokens in the first set of the following symbol j
                            foreach (var token in followingFirstSet.Where(t => t != null))
                            {
                                changed |= followSet.Add(token);
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

        public ImmutableHashSet<Token> FirstOf(Symbol symbol) => this._firstSets[symbol];
        public ImmutableHashSet<Token> FollowOf(Symbol symbol) => this._followSets[symbol];
        public ImmutableHashSet<Token> FollowOf(Rule rule) => this.FollowOf(rule.Produced);
    }
}
