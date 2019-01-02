using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;

namespace Forelle.Parsing.Construction.Ambiguity
{
    // TODO AMB comment, move to ambiguity folder

    internal class FirstFollowLastPrecedingCalculator
    {
        // TODO AMB remove field
        private readonly IReadOnlyDictionary<Rule, Rule> _ruleToReverseRules;
        private readonly IFirstFollowProvider _firstFollowProvider, _lastPrecedingProvider;

        private FirstFollowLastPrecedingCalculator(
            IReadOnlyDictionary<Rule, Rule> ruleToReverseRules,
            IFirstFollowProvider firstFollowProvider,
            IFirstFollowProvider lastPrecedingProvider)
        {
            this._ruleToReverseRules = ruleToReverseRules;
            this._firstFollowProvider = firstFollowProvider;
            this._lastPrecedingProvider = lastPrecedingProvider;
        }
        
        public static FirstFollowLastPrecedingCalculator Create(IReadOnlyCollection<Rule> rules)
        {
            var firstFollowProvider = FirstFollowCalculator.Create(rules);

            var rulesToReversedRules = rules.ToDictionary(r => r, r => new Rule(r.Produced, r.Symbols.Reverse()));
            var lastPrecedingProvider = FirstFollowCalculator.Create(rulesToReversedRules.Values);

            return new FirstFollowLastPrecedingCalculator(
                ruleToReverseRules: rulesToReversedRules,
                firstFollowProvider: firstFollowProvider,
                lastPrecedingProvider: lastPrecedingProvider
            );
        }

        public ImmutableHashSet<Token> FirstOf(Symbol symbol) => this._firstFollowProvider.FirstOf(symbol);
        public ImmutableHashSet<Token> FollowOf(Symbol symbol) => this._firstFollowProvider.FollowOf(symbol);
        
        public bool NextOfContains(RuleRemainder rule, Token contained)
        {
            for (var i = 0; i < rule.Symbols.Count; ++i)
            {
                var first = this.FirstOf(rule.Symbols[i]);
                if (first.Contains(contained)) { return true; }
                if (!first.Contains(null)) { return false; }
            }

            // if we reach here, the entire symbol list was nullable (or empty), and so we just consider the follow
            return this._firstFollowProvider.FollowOf(rule.Rule).Contains(contained);
        }

        public ImmutableHashSet<Token> LastOf(Symbol symbol) => this._lastPrecedingProvider.FirstOf(symbol);
        public ImmutableHashSet<Token> PrecedingOf(Symbol symbol) => this._lastPrecedingProvider.FollowOf(symbol);
    }
}
