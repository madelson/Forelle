using System;
using System.Collections.Generic;
using System.Text;
using System.Linq;
using Medallion.Collections;
using System.Collections.Immutable;

namespace Forelle.Parsing.Construction
{
    internal class DiscriminatorHelper
    {
        private readonly IReadOnlyDictionary<NonTerminal, IReadOnlyList<Rule>> _rulesByProduced;
        private readonly IFirstFollowProvider _firstFollowProvider;

        /// <summary>
        /// The set of <see cref="NonTerminal"/>s in <see cref="_rulesByProduced"/> last time <see cref="_discriminatorPrefixSearchTrie"/>
        /// was calculated. Used to determine when to recalculate
        /// </summary>
        private readonly HashSet<NonTerminal> _trieCacheSet = new HashSet<NonTerminal>();
        private ImmutableTrie<Symbol, Rule> _discriminatorPrefixSearchTrie = ImmutableTrie<Symbol, Rule>.Empty;

        public DiscriminatorHelper(
            IReadOnlyDictionary<NonTerminal, IReadOnlyList<Rule>> rules,
            IFirstFollowProvider firstFollowProvider)
        {
            this._rulesByProduced = rules; // no defensive copy; this will change as things are updated!
            this._firstFollowProvider = firstFollowProvider;
        }

        // TODO feels like search should be split out to a separate class

        /// <summary>
        /// Locates all discriminator symbols which have rules exactly matching each of the given rule right-hand sides.
        /// 
        /// A matched discriminator may have additional rules as well.
        /// </summary>
        public IReadOnlyCollection<NonTerminal> FindDiscriminatorByRuleSymbols(IReadOnlyCollection<IReadOnlyList<Symbol>> ruleSymbols)
        {
            Invariant.Require(ruleSymbols.Count > 0);

            var searchTrie = this.GetSearchTrie();
            HashSet<NonTerminal> results = null;
            foreach (var rule in ruleSymbols)
            {
                var ruleResults = searchTrie[rule];
                if (results == null)
                {
                    results = new HashSet<NonTerminal>(ruleResults.Select(r => r.Produced));
                }
                else
                {
                    results.IntersectWith(ruleResults.Select(r => r.Produced));
                }

                if (results.Count == 0)
                {
                    break;
                }
            }

            return results;
        }

        public List<DiscriminatorPrefixSearchResult> FindPrefixDiscriminators(IReadOnlyList<RuleRemainder> rules, Token lookaheadToken)
        {
            return this.FindDiscriminators(rules, lookaheadToken, isPrefixSearch: true)
                .ToList();
        }

        private IEnumerable<DiscriminatorPrefixSearchResult> FindDiscriminators(
            IReadOnlyCollection<RuleRemainder> rules, 
            Token lookaheadToken, 
            bool isPrefixSearch)
        {
            var produced = rules.Only(r => r.Produced);

            // first, match each rule to all discriminator rules which have the same symbols or a prefix set
            // of symbols
            var searchTrie = this.GetSearchTrie();
            var rulesToSearchResults = rules.ToDictionary(r => r, r => isPrefixSearch ? searchTrie.GetWithPrefixValues(r.Symbols) : searchTrie[r.Symbols]);

            // next, potential matches are discriminators where every rule mapped to a rule for that discriminator
            var potentialMatches = rulesToSearchResults.Values
                .Select(results => results.Select(r => r.Produced))
                .Aggregate((a, b) => a.Intersect(b))
                // a symbol cannot be a match for itself
                .Where(s => s != produced);
            
            return potentialMatches.Select(s => this.GetSearchResultOrDefault(s, rulesToSearchResults, lookaheadToken))
                .Where(r => r != null);
        }

        // TODO move this to be a doc comment on a class
        /// <summary>
        /// Attempts to find a mapping between the rules for <paramref name="discriminator"/> and <paramref name="toMap"/>
        /// that satisfies the following criteria:
        /// 
        /// * PREFIX: for all mappings, the symbols in the discriminator rule form a prefix of the symbols in the mapped rule (implied by <paramref name="ruleMatches"/>)
        /// * LONGEST-PREFIX: for all mappings, there is no discriminator rule with more symbols that would also form a prefix of the mapped rule symbols
        /// * LOOKAHEAD: for all mappings, the mapped discriminator rule has <paramref name="lookaheadToken"/> in its next set.
        ///     This is important because we need to be able to parse the mapped discriminator starting with <paramref name="lookaheadToken"/>
        /// * ALL-RULES: all rules being searched are mapped (implied by <paramref name="ruleMatches"/>)
        /// * MANY-TO-MANY: it is NOT the case that all rules are mapped to the same discriminator rule. This could
        ///     happen with any empty rule. We exclude it because it doesn't help us narrow things down at all
        /// * ALL-DISCRIMINATOR-RULES: all rules for <paramref name="discriminator"/> are mapped UNLESS they cannot appear in the context of <paramref name="lookaheadToken"/>. 
        ///     This restriction is somewhat questionable, since if we did find such a result it would simply mean that there will be a parsing error
        /// </summary>
        private DiscriminatorPrefixSearchResult GetSearchResultOrDefault(
            NonTerminal discriminator, 
            IReadOnlyDictionary<RuleRemainder, ImmutableHashSet<Rule>> ruleMatches,
            Token lookaheadToken)
        {
            // LOOKAHEAD constraint
            var validDiscriminatorRules = this._rulesByProduced[discriminator].Where(r => this._firstFollowProvider.NextOf(r).Contains(lookaheadToken))
                .ToList();

            var mapping = new Dictionary<RuleRemainder, Rule>(capacity: ruleMatches.Count);
            foreach (var kvp in ruleMatches) // ruleMatches implies PREFIX constraint
            {
                // LONGEST-PREFIX constraint
                var bestMappedRule = kvp.Value.Where(validDiscriminatorRules.Contains)
                    .MaxBy(r => r.Symbols.Count);
                if (bestMappedRule == null) { return null; }
                mapping.Add(kvp.Key, bestMappedRule);
            }

            // MANY-TO-MANY constraint
            if (mapping.Values.Take(2).Distinct().Count() < 2) { return null; }
            // ALL-DISCRIMINATOR-RULES constraint
            if (validDiscriminatorRules.Except(mapping.Values).Any()) { return null; }

            bool isFollowCompatible(RuleRemainder rule, Rule mapped)
            {
                // if the lookahead token is cannot apper in the mapped rule, then it must be in the
                // follow (since we checked above that it's in the next). In that case, we know the mapped
                // rule is nullable and will produce null. The suffix of rule will then be parsed starting
                // with the lookahead token which should always work
                if (!this._firstFollowProvider.FirstOf(mapped.Symbols).Contains(lookaheadToken))
                {
                    return true;
                }

                // otherwise, we're interested in whether the follow of the mapped rule encompasses the next
                // of the suffix. If it does, then we know that the mapped rule parser will handle all cases
                // we might encounter
                return this._firstFollowProvider.FollowOf(mapped)
                    .IsSupersetOf(this._firstFollowProvider.NextOf(rule.Skip(mapped.Symbols.Count)));
            }

            return new DiscriminatorPrefixSearchResult(mapping, isFollowCompatible: mapping.All(kvp => isFollowCompatible(kvp.Key, kvp.Value)));
        }

        public sealed class DiscriminatorPrefixSearchResult
        {
            public DiscriminatorPrefixSearchResult(
                IEnumerable<KeyValuePair<RuleRemainder, Rule>> rulesToDiscriminatorMapping,
                bool isFollowCompatible)
            {
                this.RulesToDiscriminatorRuleMapping = rulesToDiscriminatorMapping.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
                this.Discriminator = this.RulesToDiscriminatorRuleMapping.Only(kvp => kvp.Value.Produced);
                this.IsExactMatch = this.RulesToDiscriminatorRuleMapping.All(kvp => kvp.Key.Symbols.Count == kvp.Value.Symbols.Count);
                this.IsFollowCompatible = isFollowCompatible;
            }

            public NonTerminal Discriminator { get; }
            public IReadOnlyDictionary<RuleRemainder, Rule> RulesToDiscriminatorRuleMapping { get; }
            public bool IsExactMatch { get; }
            public bool IsFollowCompatible { get; }
        }

        private ImmutableTrie<Symbol, Rule> GetSearchTrie()
        {
            if (this._trieCacheSet.Count < this._rulesByProduced.Count)
            {
                foreach (var kvp in this._rulesByProduced)
                {
                    if (this._trieCacheSet.Add(kvp.Key)
                        && kvp.Key.SyntheticInfo is DiscriminatorSymbolInfo)
                    {
                        foreach (var rule in kvp.Value)
                        {
                            this._discriminatorPrefixSearchTrie = this._discriminatorPrefixSearchTrie.Add(rule.Symbols, rule);
                        }
                    }
                }
            }

            return this._discriminatorPrefixSearchTrie;
        }

        // TODO return list to enforce order?
        /// <summary>
        /// Gathers the set of <see cref="Symbol"/> lists which could form the remainder after consuming
        /// a <paramref name="prefixToken"/>
        /// </summary>
        public ILookup<IReadOnlyList<Symbol>, PotentialParseParentNode> TryGatherPostTokenSuffixes(Token prefixToken, RuleRemainder rule)
        {
            // todo cleanup old code

            var oldResult = new HashSet<IReadOnlyList<Symbol>>(EqualityComparers.GetSequenceComparer<Symbol>());
            var oldRet = this.TryGatherPostTokenSuffixesOld(prefixToken, rule, ImmutableStack<Symbol>.Empty, oldResult);

            var suffixesToExpansionPaths = new Lookup<IReadOnlyList<Symbol>, ImmutableLinkedList<RuleRemainder>>(EqualityComparers.GetSequenceComparer<Symbol>());
            var newRet = this.TryGatherPostTokenSuffixes(prefixToken, ImmutableLinkedList.Create(rule), ImmutableLinkedList<Symbol>.Empty, suffixesToExpansionPaths);
            var suffixesToDerivations = suffixesToExpansionPaths.SelectMany(g => g, (g, expansionPath) => (suffix: g.Key, expansionPath))
                .ToLookup(
                    t => t.suffix,
                    t => this.ToDerivation(t.expansionPath, startingRuleOffset: rule.Start),
                    suffixesToExpansionPaths.Comparer
                );

            if (!oldResult.SetEquals(suffixesToExpansionPaths.Select(r => r.Key)) || oldRet != newRet)
            {
                throw new InvalidOperationException("fdafsd");
            }

            return newRet
                ? suffixesToDerivations
                : null;
        }

        private bool TryGatherPostTokenSuffixes(
            Token prefixToken,
            ImmutableLinkedList<RuleRemainder> expansionPath,
            ImmutableLinkedList<Symbol> suffix,
            Lookup<IReadOnlyList<Symbol>, ImmutableLinkedList<RuleRemainder>> result)
        {
            var rule = expansionPath.Head;
            if (!this._firstFollowProvider.NextOf(rule).Contains(prefixToken))
            {
                // if the prefix token can't appear next, then we're done
                return true;
            }

            if (rule.Symbols.Count == 0) // out of symbols in the rule
            {
                // this means that we are trying to strip out our prefix token,
                // but we've reached the end of the rule and have no suffix. This means that the prefix
                // token appears in the follow which prevents us from creating a complete suffix set
                return false;
            }

            if (rule.Symbols[0] is NonTerminal nonTerminal)
            {
                // if the first symbol is a non-terminal, try each expansion of that non-terminal. We 
                // add the rest of the current rule's symbols to the suffix
                var postExpansionSuffix = suffix.PrependRange(rule.Skip(1).Symbols);
                var foundExpansionSuffixes = true;
                foreach (var expansionRule in this._rulesByProduced[nonTerminal])
                {
                    // even though we don't need all rule expansions to work, we use &= here rather than |=. This is because
                    // failing to add to result doesn't indicate failure; failure is ONLY when we reach the end of the rule 
                    // without finding the token but still could find it in the follow. Because of this, any failure indicates
                    // a potential problem. "Potential" is key since if the problem is due to the CURRENT non-terminal reducing to
                    // null, then we're ok so long as we still have symbols left in the current rule/suffix (see below)
                    foundExpansionSuffixes &= this.TryGatherPostTokenSuffixes(prefixToken, expansionPath.Prepend(expansionRule.Skip(0)), postExpansionSuffix, result);
                }

                // finally, if the symbol is nullable, we consider the case where it produces null and the 
                // lookahead appears after it. In this case we create a new expansion path that moves the start pointer
                // on the current rule forwards
                return this._firstFollowProvider.FirstOf(nonTerminal).Contains(null)
                    // if the current symbol is nullable, then we also have to succeed in the expansion that skips that
                    // symbol. If we do succeed in this, then it's ok if some of our other expansions failed. Really the only
                    // failures that matter are when we exhaust the symbols in the current rule AND the suffix is empty
                    ? this.TryGatherPostTokenSuffixes(prefixToken, expansionPath.Tail.Prepend(rule.Skip(1)), suffix, result)
                    : foundExpansionSuffixes;
            }

            // if we reach this point, then first symbol IS the token we're looking for.
            // This is because (a) we've checked that the token is in the Next set, (b)
            // We've checked that they rule has at least one symbol and (c) we've checked
            // that the symbol is not a non-terminal (and therefore is a token)
            Invariant.Require(rule.Symbols[0] == prefixToken);
            // therefore, whe can build a complete suffix set by just dropping the token
            // from the current rule and adding the rest of the symbols to the suffix
            result.Add(rule.Skip(1).Symbols.Concat(suffix).ToArray(), expansionPath);
            return true;
        }

        private PotentialParseParentNode ToDerivation(ImmutableLinkedList<RuleRemainder> expansionPath, int startingRuleOffset)
        {
            Invariant.Require(expansionPath.Count > 0);

            PotentialParseParentNode derivation = null;
            var remainingExpansions = expansionPath;
            while (remainingExpansions.TryDeconstruct(out var nextExpansion, out remainingExpansions))
            {
                derivation = new PotentialParseParentNode(
                    nextExpansion.Rule,
                    nextExpansion.Rule.Symbols.Select(
                        // if we're at the expansion point, plug in either the derivation so far OR if we have none then the marked token
                        (s, i) => i == nextExpansion.Start ? (derivation ?? new PotentialParseLeafNode(s, cursorPosition: 0).As<PotentialParseNode>())
                            // else if we're looking at the start rule and we're before the starting offset, create a regular leaf
                            : remainingExpansions.Count == 0 && i < startingRuleOffset ? PotentialParseNode.Create(s)
                            // else if we're before the expansion point, create an empty leaf since we parsed the symbols as null to skip it
                            : i < nextExpansion.Start ? new PotentialParseParentNode(this._rulesByProduced[(NonTerminal)s].Single(r => r.Symbols.Count == 0), Enumerable.Empty<PotentialParseNode>())
                            // otherwise, create a normal leaf node
                            : PotentialParseNode.Create(s)
                    )
                );
            }

            return derivation;
        }

        // todo remove
        /// <param name="prefixToken">the token to be moved past</param>
        /// <param name="rule">the current rule being expanded</param>
        /// <param name="suffix">
        /// A stack of the known symbols that follow the current rule in the expansion. The top
        /// of the stack is the first following symbol
        /// </param>
        /// <param name="result">gathers the resulting suffixes</param>
        /// <returns>
        /// True if the gathering was successful, false if we were unable to perform remove <paramref name="prefixToken"/> from some path.
        /// This can happen if we find a nullable construction which has <paramref name="prefixToken"/> in its follow set
        /// </returns>
        private bool TryGatherPostTokenSuffixesOld(
            Token prefixToken,
            RuleRemainder rule,
            ImmutableStack<Symbol> suffix,
            ISet<IReadOnlyList<Symbol>> result)
        {
            if (rule.Symbols.Count == 0) // out of symbols in the rule
            {
                if (!suffix.IsEmpty) // if we have a suffix, dig into that
                {
                    var nextSuffixSymbol = suffix.Peek();
                    if (nextSuffixSymbol is Token)
                    {
                        // if the suffix starts with a token, either it's the token we're looking for
                        // or else we've hit a dead end
                        if (nextSuffixSymbol == prefixToken)
                        {
                            result.Add(suffix.Skip(1).ToArray());
                        }
                    }
                    else
                    {
                        // else if it's a non-terminal, then recurse on each rule for that non-terminal where the next
                        // set contains the token of interest

                        var newSuffix = suffix.Pop();
                        var innerRules = this._rulesByProduced[(NonTerminal)nextSuffixSymbol]
                            .Where(r => this._firstFollowProvider.NextOf(r).Contains(prefixToken));
                        var success = true;
                        foreach (var innerRule in innerRules)
                        {
                            // we don't bail immediately on failure here because failures can be turned into successes at the
                            // end of the similar loop below
                            success &= this.TryGatherPostTokenSuffixesOld(prefixToken, innerRule.Skip(0), newSuffix, result);
                        }
                        return success;
                    }
                }
                else
                {
                    // if we get here, that means that we are trying to strip out our prefix token,
                    // but we've reached the end of the rule and have no suffix. This means that the prefix
                    // token might appear in the follow. If it does, that's a problem because we cannot
                    // create a complete suffix set
                    return !this._firstFollowProvider.FollowOf(rule.Rule).Contains(prefixToken);
                }
            }
            else if (rule.Symbols[0] is Token)
            {
                // if the first symbol of the rule is the token, then either it's the 
                // token of interest or we're done
                if (rule.Symbols[0] == prefixToken)
                {
                    result.Add(rule.Symbols.Skip(1).Concat(suffix).ToArray());
                }
            }
            else // the first symbol of the rule is a non-terminal
            {
                // recurse on each rule for that symbol

                // the new suffix adds the rest of the current rule, from back to front
                // to preserve ordering
                var newSuffix = suffix;
                for (var i = rule.Symbols.Count - 1; i > 0; --i)
                {
                    newSuffix = newSuffix.Push(rule.Symbols[i]);
                }

                var innerRules = this._rulesByProduced[(NonTerminal)rule.Symbols[0]]
                    .Where(r => this._firstFollowProvider.NextOf(r).Contains(prefixToken));
                var success = true;
                foreach (var innerRule in innerRules)
                {
                    success &= this.TryGatherPostTokenSuffixesOld(prefixToken, innerRule.Skip(0), newSuffix, result);
                }
                // the return value here is true if all recursive calls succeeded or if the current rule cannot 
                // be followed by the prefix token. Note that we don't really expect this to come into play since the way
                // follow sets are computed we would expect that any time false was returned by the recursive call then
                // prefixToken would be in the follow (since the follow of the outer rule should incorporate follows of all
                // terminating inner rules). However, checking this check does no harm and doesn't make as many assumptions about the
                // follow set calculation
                return success || !this._firstFollowProvider.FollowOf(rule.Rule).Contains(prefixToken);
            }

            return true;
        }
    }
}
