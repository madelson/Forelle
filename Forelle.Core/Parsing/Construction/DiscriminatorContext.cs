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
    internal sealed class DiscriminatorContext
    {
        public DiscriminatorContext(
            IEnumerable<(Rule DiscriminatorRule, RuleRemainder MappedRule)> ruleMapping,
            Token lookaheadToken,
            bool isPrefix)
        {
            this.RuleMapping = (ruleMapping ?? throw new ArgumentNullException(nameof(ruleMapping)))
                .ToArray();
            this.LookaheadToken = lookaheadToken ?? throw new ArgumentNullException(nameof(lookaheadToken));

            // sanity checks
            if (!(this.RuleMapping.Only(m => m.DiscriminatorRule.Produced.SyntheticInfo) is DiscriminatorSymbolInfo))
            {
                throw new ArgumentException(nameof(ruleMapping), "the discriminator rule must produce a discriminator symbol");
            }
            if (isPrefix
                && !this.RuleMapping.All(m => IsValidPrefixMapping(m.DiscriminatorRule, m.MappedRule)))
            {
                throw new ArgumentException(nameof(isPrefix), "all discriminator rules must be prefixes of the mapped rules");
            }
            this.IsPrefix = isPrefix;
        }
        
        /// <summary>
        /// The mapping between discriminator <see cref="Rule"/>s and the <see cref="Rule"/>s
        /// they discriminate between. Note that if <see cref="IsPrefix"/>, the mapped <see cref="Rule"/>
        /// will be the remainder of the rule to get parsed after the discriminator <see cref="Rule"/>
        /// has been consumed
        /// </summary>
        public IReadOnlyList<(Rule DiscriminatorRule, RuleRemainder MappedRule)> RuleMapping { get; }

        /// <summary>
        /// For an <see cref="IsPrefix"/> discriminator context, this is the next token appearing in the
        /// stream when we begin parsing the discriminator. 
        /// 
        /// Otherwise, this is the token that the discriminator lookahead jumps over before we begin parsing
        /// the discriminator
        /// </summary>
        public Token LookaheadToken { get; }

        /// <summary>
        /// Specifies whether the discriminator is being used as a prefix of another discriminator
        /// </summary>
        public bool IsPrefix { get; }

        private static bool IsValidPrefixMapping(Rule discriminatorRule, RuleRemainder mappedRule)
        {
            return mappedRule.Rule.Skip(mappedRule.Start - discriminatorRule.Symbols.Count).Symbols
                .Take(discriminatorRule.Symbols.Count)
                .SequenceEqual(discriminatorRule.Symbols);
        }
    }
}
