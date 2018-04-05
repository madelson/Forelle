using Medallion.Collections;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;

namespace Forelle.Parsing.Construction
{
    internal class AmbiguityContextualizer
    {
        private readonly IReadOnlyDictionary<NonTerminal, IReadOnlyList<Rule>> _rulesByProduced;
        private readonly IFirstFollowProvider _firstFollowProvider;
        private readonly ILookup<NonTerminal, DiscriminatorContext> _discriminatorContexts;

        /// <summary>
        /// Maps all <see cref="Symbol"/>s to the set of non-discriminator rules where they are referenced and
        /// the index positions of those references in the rule
        /// </summary>
        private readonly Lazy<ILookup<Symbol, (Rule rule, int index)>> _nonDiscriminatorSymbolReferences;

        public AmbiguityContextualizer(
            IReadOnlyDictionary<NonTerminal, IReadOnlyList<Rule>> rulesByProduced,
            IFirstFollowProvider firstFollowProvider,
            ILookup<NonTerminal, DiscriminatorContext> discriminatorContexts)
        {
            // no defensive copies here: we are ok with the state changing
            this._rulesByProduced = rulesByProduced;
            this._firstFollowProvider = firstFollowProvider;
            this._discriminatorContexts = discriminatorContexts;

            // since the only rules that get added along the way are for discriminators, we can safely
            // build this cache only once
            this._nonDiscriminatorSymbolReferences = new Lazy<ILookup<Symbol, (Rule rule, int index)>>(
                () => this._rulesByProduced.Where(kvp => !(kvp.Key.SyntheticInfo is DiscriminatorSymbolInfo))
                    .SelectMany(kvp => kvp.Value)
                    .SelectMany(r => r.Symbols.Select((s, i) => (referenced: s, index: i, rule: r)))
                    .ToLookup(t => t.referenced, t => (rule: t.rule, index: t.index))
            );
        }

        public List<List<PotentialParseNode>> GetExpandedAmbiguityContexts(IReadOnlyList<RuleRemainder> rules, Token lookaheadToken)
        {
            Guard.NotNullOrContainsNull(rules, nameof(rules));
            if (lookaheadToken == null) { throw new ArgumentNullException(nameof(lookaheadToken)); }

            //if ("a"[0] == 'a') { throw new NotImplementedException(); }

            var results = rules.Select(r => this.GetExpandedContexts(
                    r,
                    r.Rule.Symbols.Take(r.Start).Select(s => new PotentialParseLeafNode(s)).ToArray(),
                    lookaheadToken
                )
                .ToArray()
            )
            .ToArray();
            throw new NotImplementedException();
        }
        
        private IEnumerable<PotentialParseParentNode> GetExpandedContexts(
            RuleRemainder rule,
            IEnumerable<PotentialParseNode> prefixParse,
            Token lookaheadToken)
        {
            // if we're looking at a discriminator rule, handle that
            if (rule.Produced.SyntheticInfo is DiscriminatorSymbolInfo)
            {
                return this.GetExpandedDiscriminatorContexts(rule, prefixParse, lookaheadToken);
            }

            // otherwise, check if the token can appear in the rest of the rule
            var innerContexts = this.GetExpandedFirstContexts(rule, prefixParse, lookaheadToken);

            // then, check if the token can next appear after the rule
            var outerContexts = this._firstFollowProvider.FirstOf(rule.Symbols).ContainsNull()
                ? this.GetExpandedFollowContexts(rule.Rule, prefixParse, lookaheadToken)
                : Enumerable.Empty<PotentialParseParentNode>();

            return outerContexts.Append(innerContexts);
        }

        private IEnumerable<PotentialParseParentNode> GetExpandedDiscriminatorContexts(
            RuleRemainder discriminatorRule,
            IEnumerable<PotentialParseNode> prefixParse,
            Token lookaheadToken)
        {
            return this._discriminatorContexts[discriminatorRule.Produced].SelectMany(c => this.GetExpandedDiscriminatorContexts(discriminatorRule, c, prefixParse, lookaheadToken));
        }

        private IEnumerable<PotentialParseParentNode> GetExpandedDiscriminatorContexts(
            RuleRemainder discriminatorRule,
            DiscriminatorContext discriminatorContext,
            IEnumerable<PotentialParseNode> prefixParse,
            Token lookaheadToken)
        {
            var ruleMapping = discriminatorContext.RuleMappings.Single(t => t.DiscriminatorRule == discriminatorRule.Rule);
            var mappedRule = ruleMapping.MappedRule;
            
            // if this is a prefix, then we just need to properly set the start position of the mapped rule
            // based on the start position of the input rule and recurse
            if (discriminatorContext is PrefixDiscriminatorContext)
            {
                return this.GetExpandedContexts(
                    rule: mappedRule.Rule.Skip(mappedRule.Start - discriminatorRule.Symbols.Count),
                    // to form the prefix, we use default parses for everything before prefixParse followed by prefixParse.
                    // Note that this is subtly different from what the line above does because the line above is adding in
                    // discriminatorRule.Start to reflect additional prefix symbols whereas here we are ignoring it
                    prefixParse: mappedRule.Rule.Symbols.Take(mappedRule.Start - discriminatorRule.Rule.Symbols.Count)
                        .Select(s => new PotentialParseLeafNode(s))
                        .Append(prefixParse),
                    lookaheadToken: lookaheadToken
                );
            }

            // otherwise, discriminator is a suffix after eating the lookahead token

            // next, we consider the simpler case where the discriminator pulled a token directly off the mapped rule
            if (mappedRule.Symbols[0] == discriminatorContext.LookaheadToken)
            {
                // in this case, we can simply align and recurse on the mapped rule
                return this.GetExpandedContexts(
                    // the discriminator rule here is a suffix of the mapped rule so the aligned position is just
                    // the discriminator rule position + 1 for the lookahead token
                    rule: mappedRule.Skip(discriminatorRule.Start + 1),
                    // the prefix includes all symbols in the mapped rule prior to the start of the discriminator
                    prefixParse: mappedRule.Rule.Symbols.Take(mappedRule.Rule.Symbols.Count - discriminatorRule.Rule.Symbols.Count)
                        .Select(s => new PotentialParseLeafNode(s))
                        .Append(prefixParse),
                    lookaheadToken: lookaheadToken
                );
            }

            // finally, we consider the case where in shifting the lookahead token the discriminator had to expand the first symbol.
            // In this case, we need to determine whether the discriminator rule position comes before or after this expansion
            var expansionPaths = ((PostTokenSuffixDiscriminatorContext.RuleMapping)ruleMapping).ExpansionPaths;
            return expansionPaths.SelectMany(expansionPath => this.GetExpandedDiscriminatorContexts(
                discriminatorRule,
                expansionPath,
                expansionPathIndex: 0,
                prefixParse: prefixParse,
                lookaheadToken: lookaheadToken
            ));
        }

        private IEnumerable<PotentialParseParentNode> GetExpandedDiscriminatorContexts(
            RuleRemainder discriminatorRule,
            IReadOnlyList<RuleRemainder> expansionPath,
            int expansionPathIndex,
            IEnumerable<PotentialParseNode> prefixParse,
            Token lookaheadToken)
        {
            var expansionRule = expansionPath[expansionPathIndex];


            throw new NotImplementedException();
        }

        private IEnumerable<PotentialParseParentNode> GetExpandedFirstContexts(
            RuleRemainder rule,
            IEnumerable<PotentialParseNode> prefixParse,
            Token lookaheadToken)
        {
            // if the lookahead can't appear, we're done
            if (!this._firstFollowProvider.FirstOf(rule.Symbols).Contains(lookaheadToken))
            {
                return Enumerable.Empty<PotentialParseParentNode>();
            }

            var next = rule.Symbols[0];
            var rest = rule.Skip(1);

            // if the next symbol is the target token, then the only potential parse sequence
            // is the current sequence
            if (next == lookaheadToken)
            {
                return new[] { new PotentialParseParentNode(rule.Rule, prefixParse.Append(rule.Symbols.Select(s => new PotentialParseLeafNode(s)))) };
            }

            // otherwise, there are two possible routes:

            // (1) expand productions of next
            var expandedNextContexts = this._rulesByProduced[(NonTerminal)next]
                .Select(r => this.GetExpandedFirstContexts(
                    rule: r.Skip(0),
                    prefixParse: Enumerable.Empty<PotentialParseNode>(),
                    lookaheadToken: lookaheadToken)
                )
                .Select(p => new PotentialParseParentNode(
                    rule.Rule, 
                    rule.Symbols.Select(s => new PotentialParseLeafNode(s)).Prepend<PotentialParseNode>(p).Prepend(prefixParse))
                );

            // (2) if next is nullable, expand productions of rest using the empty production of next
            var expandedRestContexts = this._firstFollowProvider.FirstOf(next).ContainsNull()
                ? this.GetExpandedFirstContexts(
                    rule: rest,
                    prefixParse: prefixParse.Append(this.EmptyParseOf((NonTerminal)next)),
                    lookaheadToken: lookaheadToken
                )
                : Enumerable.Empty<PotentialParseParentNode>();

            return expandedNextContexts.Append(expandedRestContexts);
        }

        private IEnumerable<PotentialParseParentNode> GetExpandedFollowContexts(
            Rule rule,
            IEnumerable<PotentialParseNode> prefixParse,
            Token lookaheadToken)
        {
            // if the lookahead can't appear, we're done
            if (!this._firstFollowProvider.FollowOf(rule).Contains(lookaheadToken))
            {
                return Enumerable.Empty<PotentialParseParentNode>();
            }

            // otherwise, then just find all rules that reference the current rule and expand them

            // we know that the current rule was parsed using the given prefix and then null productions for all remaining symbols (since we're looking at follow)
            var ruleParse = new PotentialParseParentNode(
                rule,
                prefixParse.Concat(rule.Symbols.Select(s => this.EmptyParseOf((NonTerminal)s)))
            );

            return this._nonDiscriminatorSymbolReferences.Value[rule.Produced]
                .SelectMany(reference => this.GetExpandedContexts(
                    rule: reference.rule.Skip(reference.index + 1),
                    // build the prefix leveraging the structure we know for the current node and assuming default
                    // structure for the rest
                    prefixParse: reference.rule.Symbols.Take(reference.index)
                        .Select(s => new PotentialParseLeafNode(s))
                        .Append<PotentialParseNode>(ruleParse),
                    lookaheadToken: lookaheadToken
                ));
        }

        private PotentialParseParentNode EmptyParseOf(NonTerminal produced)
        {
            return new PotentialParseParentNode(
                this._rulesByProduced[produced].Single(r => r.Symbols.Count == 0), 
                Enumerable.Empty<PotentialParseNode>()
            );
        }
        
        internal abstract class PotentialParseNode
        {
            protected PotentialParseNode(Symbol symbol)
            {
                if (symbol == null) { throw new ArgumentNullException(nameof(symbol)); }
                if (symbol.IsSynthetic) { throw new ArgumentException("must not be synthetic", nameof(symbol)); }

                this.Symbol = symbol;
            }

            public Symbol Symbol { get; }

            public abstract IEnumerable<PotentialParseLeafNode> EnumerateLeafNodes();
        }
        
        internal sealed class PotentialParseLeafNode : PotentialParseNode
        {
            public PotentialParseLeafNode(Symbol symbol)
                : base(symbol)
            {
            }

            public override IEnumerable<PotentialParseLeafNode> EnumerateLeafNodes() { yield return this; }

            public override string ToString() => $"\"{this.Symbol.Name}\"";
        }

        internal sealed class PotentialParseParentNode : PotentialParseNode
        {
            public PotentialParseParentNode(Rule rule, IEnumerable<PotentialParseNode> children)
                : base(rule.Produced)
            {
                this.Rule = rule;
                this.Children = Guard.NotNullOrContainsNullAndDefensiveCopy(children, nameof(children));

                if (this.Rule.Symbols.SequenceEqual(this.Children.Select(c => c.Symbol))) { throw new ArgumentException("rule symbols and child symbols must match"); }
            }

            public Rule Rule { get; }

            public IReadOnlyList<PotentialParseNode> Children { get; }

            public override IEnumerable<PotentialParseLeafNode> EnumerateLeafNodes()
            {
                foreach (var child in this.Children)
                {
                    foreach (var leaf in child.EnumerateLeafNodes())
                    {
                        yield return leaf;
                    }
                }
            }

            public override string ToString() => $"\"{this.Symbol.Name}\"({string.Join(", ", this.Children)})";
        }
    }
}
