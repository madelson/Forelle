using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Forelle.Parsing.Construction
{
    /// <summary>
    /// Describes how a discriminator <see cref="NonTerminal"/> is used. Any discriminator
    /// can be used in multiple ways (e. g. as both a prefix and a lookahead), so it can have
    /// multiple <see cref="DiscriminatorContext"/>s
    /// </summary>
    internal abstract class DiscriminatorContext
    {
        protected DiscriminatorContext(
            Token lookaheadToken,
            IReadOnlyList<RuleMapping> ruleMappings)
        {
            this.LookaheadToken = lookaheadToken ?? throw new ArgumentNullException(nameof(lookaheadToken));
            this.RuleMappings = ruleMappings; // nullity validated by caller

            if (this.RuleMappings.Count < 2)
            {
                throw new ArgumentException("must have at least two mappings", nameof(ruleMappings));
            }
            if (this.RuleMappings.Select(m => m.DiscriminatorRule.Produced).Distinct().Count() > 1)
            {
                throw new ArgumentException("must all map rules for the same discriminator", nameof(ruleMappings));
            }
        }

        /// <summary>
        /// The mappings between discriminator <see cref="Rule"/>s and the <see cref="Rule"/>s
        /// they discriminate between. Note that if <see cref="IsPrefix"/>, the mapped <see cref="Rule"/>
        /// will be the remainder of the rule to get parsed after the discriminator <see cref="Rule"/>
        /// has been consumed
        /// </summary>
        public IReadOnlyList<RuleMapping> RuleMappings { get; }
        
        /// <summary>
        /// For an <see cref="IsPrefix"/> discriminator context, this is the next token appearing in the
        /// stream when we begin parsing the discriminator. 
        /// 
        /// Otherwise, this is the token that the discriminator lookahead jumps over before we begin parsing
        /// the discriminator
        /// </summary>
        public Token LookaheadToken { get; }
        
        public abstract class RuleMapping
        {
            protected RuleMapping(Rule discriminatorRule, RuleRemainder mappedRule)
            {
                this.DiscriminatorRule = discriminatorRule ?? throw new ArgumentNullException(nameof(discriminatorRule));
                this.MappedRule = mappedRule ?? throw new ArgumentNullException(nameof(mappedRule));

                if (!(this.DiscriminatorRule.Produced.SyntheticInfo is DiscriminatorSymbolInfo))
                {
                    throw new ArgumentException("must produce a discriminator symbol", nameof(discriminatorRule));
                }
                if (this.MappedRule.Produced == this.DiscriminatorRule.Produced)
                {
                    throw new ArgumentException("must map to a different non-terminal");
                }
            }

            public Rule DiscriminatorRule { get; }
            public RuleRemainder MappedRule { get; }
        }
    }

    internal sealed class PostTokenSuffixDiscriminatorContext : DiscriminatorContext
    {
        public PostTokenSuffixDiscriminatorContext(
            IEnumerable<RuleMapping> ruleMappings,
            Token lookaheadToken)
            : base(lookaheadToken, Guard.NotNullOrContainsNullAndDefensiveCopy(ruleMappings, nameof(ruleMappings)))
        {
            foreach (var ruleMapping in ruleMappings)
            {
                foreach (var expansionPath in ruleMapping.ExpansionPaths)
                {
                    var lastSegment = expansionPath[expansionPath.Count - 1];
                    if (lastSegment.Symbols[0] != lookaheadToken)
                    {
                        throw new ArgumentException($"invalid expansion path {string.Join(" -> ", expansionPath)}: must end with the lookahead token", nameof(ruleMappings));
                    }
                }
            }
        }

        public new IReadOnlyList<RuleMapping> RuleMappings => (IReadOnlyList<RuleMapping>)base.RuleMappings;

        public new sealed class RuleMapping : DiscriminatorContext.RuleMapping
        {
            public RuleMapping(
                Rule discriminatorRule,
                RuleRemainder mappedRule,
                IEnumerable<IReadOnlyList<RuleRemainder>> expansionPaths)
                : base(discriminatorRule, mappedRule)
            {
                this.ExpansionPaths = expansionPaths?.ToArray() ?? throw new ArgumentNullException(nameof(expansionPaths));
                
                if (this.ExpansionPaths.Count == 0) { throw new ArgumentException("must not be empty", nameof(expansionPaths)); }
                foreach (var expansionPath in this.ExpansionPaths)
                {
                    Guard.NotNullOrContainsNull(expansionPath, nameof(expansionPaths));
                    if (expansionPath[0].Rule != this.MappedRule.Rule) { throw new ArgumentException("invalid expansion path start", nameof(expansionPaths)); }
                }
            }
            
            public IReadOnlyList<IReadOnlyList<RuleRemainder>> ExpansionPaths { get; }
        }
    }

    internal sealed class PrefixDiscriminatorContext : DiscriminatorContext
    {
        public PrefixDiscriminatorContext(
            IEnumerable<RuleMapping> ruleMappings,
            Token lookaheadToken)
            : base(lookaheadToken, Guard.NotNullOrContainsNullAndDefensiveCopy(ruleMappings, nameof(ruleMappings)))
        {
        }

        public new IReadOnlyList<RuleMapping> RuleMappings => (IReadOnlyList<RuleMapping>)base.RuleMappings;

        /// <summary>
        /// The mapped <see cref="Rule"/> will be the remainder of the rule to 
        /// get parsed after the discriminator <see cref="Rule"/> has been consumed
        /// </summary>
        public new sealed class RuleMapping : DiscriminatorContext.RuleMapping
        {
            public RuleMapping(Rule discriminatorRule, RuleRemainder mappedRule)
                : base(discriminatorRule, mappedRule)
            {
                if (!(mappedRule.Produced.SyntheticInfo is DiscriminatorSymbolInfo))
                {
                    throw new ArgumentException("prefix rule mapping must map to a discriminator rule");
                }

                if (!IsValidPrefixMapping(discriminatorRule, mappedRule))
                {
                    throw new ArgumentException("discriminator rule must be a prefix of the mapped rule", nameof(discriminatorRule));
                }
            }

            private static bool IsValidPrefixMapping(Rule discriminatorRule, RuleRemainder mappedRule)
            {
                // in the prefix case, discriminatorRule forms a prefix of mappedRule.Rule, where mappedRule itself
                // is a REMAINDER following the prefix

                return mappedRule.Rule.Skip(mappedRule.Start - discriminatorRule.Symbols.Count).Symbols
                    .Take(discriminatorRule.Symbols.Count)
                    .SequenceEqual(discriminatorRule.Symbols);
            }
        }
    }
}
