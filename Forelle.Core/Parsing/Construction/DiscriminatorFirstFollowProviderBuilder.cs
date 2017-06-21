using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text;

namespace Forelle.Parsing.Construction
{
    /// <summary>
    /// Implements a <see cref="IFirstFollowProvider"/> to which discriminator symbols
    /// can be added
    /// </summary>
    internal class DiscriminatorFirstFollowProviderBuilder : IFirstFollowProvider
    {
        private readonly IFirstFollowProvider _baseProvider;
        private readonly Dictionary<NonTerminal, ImmutableHashSet<Token>> _addedFirstSets = new Dictionary<NonTerminal, ImmutableHashSet<Token>>();
        /// <summary>
        /// Since discriminator and discriminator prefix tokens never appear on the right-hand side of a rule, there is no reason
        /// to think about FOLLOW(`TX). Instead, we should consider only FOLLOW(`TX -> x) for each rule `TX -> x. The advantage of this is
        /// that we get better differentiation: we can now distinguish between multiple nullable rules if they have different follows
        /// </summary>
        private readonly Dictionary<Rule, ImmutableHashSet<Token>> _addedFollowSets = new Dictionary<Rule, ImmutableHashSet<Token>>();

        public DiscriminatorFirstFollowProviderBuilder(IFirstFollowProvider baseProvider)
        {
            this._baseProvider = baseProvider;
        }

        public void Add(IReadOnlyDictionary<Rule, ImmutableHashSet<Token>> symbolRulesAndFollowSets)
        {
            var produced = symbolRulesAndFollowSets.Only(kvp => kvp.Key.Produced);
            if (!(produced.SyntheticInfo is DiscriminatorSymbolInfo))
            {
                throw new ArgumentException("must be a discriminator symbol", nameof(produced));
            }

            var firstSetBuilder = ImmutableHashSet.CreateBuilder<Token>();
            foreach (var kvp in symbolRulesAndFollowSets)
            {
                firstSetBuilder.UnionWith(this.FirstOf(kvp.Key.Symbols));
                this._addedFollowSets.Add(kvp.Key, kvp.Value);
            }

            this._addedFirstSets.Add(produced, firstSetBuilder.ToImmutable());
        }

        public ImmutableHashSet<Token> FirstOf(Symbol symbol)
        {
            return symbol is NonTerminal nonTerminal && this._addedFirstSets.TryGetValue(nonTerminal, out var added)
                ? added
                : this._baseProvider.FirstOf(symbol);
        }

        public ImmutableHashSet<Token> FollowOf(Symbol symbol)
        {
            if (symbol is NonTerminal nonTerminal 
                && this._addedFirstSets.ContainsKey(nonTerminal))
            {
                throw new ArgumentException($"Should not ask for the follow of synthetic symbol '{symbol}'", nameof(symbol));
            }

            return this._baseProvider.FollowOf(symbol);
        }

        public ImmutableHashSet<Token> FollowOf(Rule rule)
        {
            return this._addedFollowSets.TryGetValue(rule, out var added)
                ? added
                : this._baseProvider.FollowOf(rule);
        }
    }
}
