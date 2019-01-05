using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;

namespace Forelle.Parsing.Construction.Ambiguity
{
    /// <summary>
    /// This class builds on the capabilities of <see cref="IFirstFollowProvider"/> by allowing us to look backwards in the grammar as well
    /// (last and preceding sets). This is accomplished simply by building a <see cref="IFirstFollowProvider"/> for a grammar where all productions
    /// have their symbol lists reversed!
    /// 
    /// Note that currently this class does not support by-rule sets (e. g. <see cref="IFirstFollowProvider.FollowOf(Rule)"/>)
    /// </summary>
    internal class FirstFollowLastPrecedingCalculator
    {
        private readonly IFirstFollowProvider _firstFollowProvider, _lastPrecedingProvider;

        private FirstFollowLastPrecedingCalculator(
            IFirstFollowProvider firstFollowProvider,
            IFirstFollowProvider lastPrecedingProvider)
        {
            this._firstFollowProvider = firstFollowProvider;
            this._lastPrecedingProvider = lastPrecedingProvider;
        }
        
        public static FirstFollowLastPrecedingCalculator Create(IReadOnlyCollection<Rule> rules)
        {
            var firstFollowProvider = FirstFollowCalculator.Create(rules);

            var reversedRules = rules.Select(r => new Rule(r.Produced, r.Symbols.Reverse())).ToArray();
            var lastPrecedingProvider = FirstFollowCalculator.Create(reversedRules);

            return new FirstFollowLastPrecedingCalculator(
                firstFollowProvider: firstFollowProvider,
                lastPrecedingProvider: lastPrecedingProvider
            );
        }

        public ImmutableHashSet<Token> FirstOf(Symbol symbol) => this._firstFollowProvider.FirstOf(symbol);
        public ImmutableHashSet<Token> FollowOf(Symbol symbol) => this._firstFollowProvider.FollowOf(symbol);
        
        /// <summary>
        /// Similar to <see cref="FirstFollowProviderExtensions.NextOf(IFirstFollowProvider, RuleRemainder)"/>, 
        /// but avoids building up a new <see cref="ImmutableHashSet{T}"/>.
        /// </summary>
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
