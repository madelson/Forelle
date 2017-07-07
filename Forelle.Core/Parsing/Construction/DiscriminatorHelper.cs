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
        private readonly IReadOnlyDictionary<NonTerminal, IReadOnlyList<Rule>> _rules;
        private readonly IFirstFollowProvider _firstFollowProvider;

        public DiscriminatorHelper(
            IReadOnlyDictionary<NonTerminal, IReadOnlyList<Rule>> rules,
            IFirstFollowProvider firstFollowProvider)
        {
            this._rules = rules; // no defensive copy; this will change as things are updated!
            this._firstFollowProvider = firstFollowProvider;
        }

        public object TryFindDiscriminator(IReadOnlyList<RuleRemainder> rules, Token lookaheadToken)
        {
            var discriminatorSymbols = this._rules.Keys.Select(s => (symbol: s, info: s.SyntheticInfo as DiscriminatorSymbolInfo))
                .Where(t => t.info != null)
                .ToDictionary(t => t.symbol, t => t.info);



            throw new NotImplementedException();
        }

        /// <summary>
        /// Gathers the set of <see cref="Symbol"/> lists which could form the remainder after consuming
        /// a <paramref name="prefixToken"/>
        /// </summary>
        public HashSet<IReadOnlyList<Symbol>> TryGatherPostTokenSuffixes(Token prefixToken, RuleRemainder rule)
        {
            var result = new HashSet<IReadOnlyList<Symbol>>(EqualityComparers.GetSequenceComparer<Symbol>());
            return this.TryGatherPostTokenSuffixes(prefixToken, rule, ImmutableStack<Symbol>.Empty, result)
                ? result
                : null;
        }

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
        private bool TryGatherPostTokenSuffixes(
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
                        var innerRules = this._rules[(NonTerminal)nextSuffixSymbol]
                            .Where(r => this._firstFollowProvider.NextOf(r).Contains(prefixToken));
                        var success = true;
                        foreach (var innerRule in innerRules)
                        {
                            // we don't bail immediately on failure here because failures can be turned into successes at the
                            // end of the similar loop below
                            success &= this.TryGatherPostTokenSuffixes(prefixToken, new RuleRemainder(innerRule, start: 0), newSuffix, result);
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

                var innerRules = this._rules[(NonTerminal)rule.Symbols[0]]
                    .Where(r => this._firstFollowProvider.NextOf(r).Contains(prefixToken));
                var success = true;
                foreach (var innerRule in innerRules)
                {
                    success &= this.TryGatherPostTokenSuffixes(prefixToken, new RuleRemainder(innerRule, start: 0), newSuffix, result);
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
