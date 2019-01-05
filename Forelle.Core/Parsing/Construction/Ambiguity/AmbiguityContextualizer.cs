using Medallion.Collections;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;

namespace Forelle.Parsing.Construction.Ambiguity
{
    /// <summary>
    /// This class takes in an ambiguity point and "contextualizes" it by replacing all discriminator symbols
    /// with the contexts in which those symbols appear
    /// </summary>
    internal class AmbiguityContextualizer
    {
        private readonly IReadOnlyDictionary<NonTerminal, IReadOnlyList<Rule>> _rulesByProduced;
        private readonly IFirstFollowProvider _firstFollowProvider;
        private readonly ILookup<NonTerminal, DiscriminatorContext> _discriminatorContexts;
        
        public AmbiguityContextualizer(
            IReadOnlyDictionary<NonTerminal, IReadOnlyList<Rule>> rulesByProduced,
            IFirstFollowProvider firstFollowProvider,
            ILookup<NonTerminal, DiscriminatorContext> discriminatorContexts)
        {
            // no defensive copies here: we are ok with the state changing
            this._rulesByProduced = rulesByProduced;
            this._firstFollowProvider = firstFollowProvider;
            this._discriminatorContexts = discriminatorContexts;
        }

        public Dictionary<RuleRemainder, PotentialParseParentNode[]> GetAmbiguityContexts(IReadOnlyList<RuleRemainder> rules, Token lookaheadToken)
        {
            Guard.NotNullOrContainsNull(rules, nameof(rules));
            if (lookaheadToken == null) { throw new ArgumentNullException(nameof(lookaheadToken)); }
            
            var results = rules.ToDictionary(
                r => r,
                r => this.ExpandDiscriminatorContexts(DefaultMarkedParseOf(r), lookaheadToken, ImmutableHashSet<(Rule from, RuleRemainder to, bool isPrefix)>.Empty).ToArray()
            );
            
            return results;
        }
        
        private IReadOnlyList<PotentialParseParentNode> ExpandDiscriminatorContexts(
            PotentialParseParentNode possibleDiscriminatorNode, 
            Token lookaheadToken,
            ImmutableHashSet<(Rule from, RuleRemainder to, bool isPrefix)> alreadyExpandedRuleMappings)
        {
            if (!(possibleDiscriminatorNode.Symbol.SyntheticInfo is DiscriminatorSymbolInfo))
            {
                return new[] { possibleDiscriminatorNode }; // no-op
            }

            var results = this._discriminatorContexts[possibleDiscriminatorNode.Rule.Produced]
                .SelectMany(c => this.ExpandDiscriminatorContext(possibleDiscriminatorNode, c, lookaheadToken, alreadyExpandedRuleMappings))
                .ToArray();
            return results;
        }

        private IReadOnlyList<PotentialParseParentNode> ExpandDiscriminatorContext(
            PotentialParseParentNode discriminatorNode,
            DiscriminatorContext discriminatorContext,
            Token lookaheadToken,
            ImmutableHashSet<(Rule from, RuleRemainder to, bool isPrefix)> alreadyExpandedRuleMappings)
        {
            // note that this can be empty in the case where a particular prefix context only maps a portion of the rules
            var ruleMappings = discriminatorContext.RuleMappings.Where(t => t.DiscriminatorRule == discriminatorNode.Rule);

            return ruleMappings.SelectMany(m => this.ExpandDiscriminatorContextRuleMapping(discriminatorNode, discriminatorContext, m, lookaheadToken, alreadyExpandedRuleMappings))
                .ToArray();
        }

        private IReadOnlyList<PotentialParseParentNode> ExpandDiscriminatorContextRuleMapping(
            PotentialParseParentNode discriminatorNode,
            DiscriminatorContext discriminatorContext,
            DiscriminatorContext.RuleMapping ruleMapping,
            Token lookaheadToken,
            ImmutableHashSet<(Rule from, RuleRemainder to, bool isPrefix)> alreadyExpandedRuleMappings)
        {
            var mappedRule = ruleMapping.MappedRule;
            var mappingEntry = (from: ruleMapping.DiscriminatorRule, to: ruleMapping.MappedRule, isPrefix: discriminatorContext is PrefixDiscriminatorContext);
            if (alreadyExpandedRuleMappings.Contains(mappingEntry))
            {
                return Array.Empty<PotentialParseParentNode>();
            }

            if (mappingEntry.isPrefix)
            {
                // we have a mapping like T0 => x maps to T1 => ... y. Therefore we have ... symbols
                // to put in, followed by symbols from the prefix discriminator, followed by any symbols in the 
                // mapped rule that follow the prefix
                var prePrefixSymbolCount = mappedRule.Start - mappingEntry.from.Symbols.Count;
                var adjustedNode = new PotentialParseParentNode(
                        mappedRule.Rule,
                        mappedRule.Rule.Symbols.Take(mappedRule.Start - mappingEntry.from.Symbols.Count)
                            .Select(s => new PotentialParseLeafNode(s))
                            .Concat(discriminatorNode.Children.Select(ch => ch.WithoutCursor()))
                            .Concat(mappedRule.Symbols.Select(s => new PotentialParseLeafNode(s)))
                    )
                    // reset the cursor in case it was trailing; it may no longer be!
                    .WithCursor(prePrefixSymbolCount + discriminatorNode.CursorPosition.Value);

                // for a prefix, just expand
                var prefixResult = this.ExpandDiscriminatorContexts(
                    adjustedNode,
                    lookaheadToken,
                    alreadyExpandedRuleMappings.Add(mappingEntry)
                );
                return prefixResult;
            }

            // otherwise, expand using the expansion path and then do a normal expansion
            var derivations = ((PostTokenSuffixDiscriminatorContext.RuleMapping)ruleMapping).Derivations;
            var expandedDerivations = derivations.Select(d => this.ExpandDiscriminatorDerivation(discriminatorNode, derivation: d));
            var results = expandedDerivations.SelectMany(d => this.ExpandDiscriminatorContexts(d, lookaheadToken, alreadyExpandedRuleMappings.Add(mappingEntry)))
                .ToArray();
            return results;
        }

        private PotentialParseParentNode ExpandDiscriminatorDerivation(
            PotentialParseParentNode discriminatorNode,
            PotentialParseParentNode derivation)
        {
            var nodesToIncorporate = new Queue<PotentialParseNode>(discriminatorNode.Children);
            var expanded = this.ExpandDiscriminatorDerivation(nodesToIncorporate, derivation);
            // in most cases, expanded will incorporate the cursor automatically because one of
            // nodesToIncorporate will have it. The exception is when discriminatorNode has zero children
            // AND a trailing cursor. Thus, we add it if needed (WithTrailingCursor is a noop if expanded
            // already has the trailing cursor)
            var result = discriminatorNode.HasTrailingCursor()
                ? (PotentialParseParentNode)expanded.WithTrailingCursor()
                : expanded;
            Invariant.Require(result.CursorPosition.HasValue);
            return result;
        }

        private PotentialParseParentNode ExpandDiscriminatorDerivation(
            Queue<PotentialParseNode> nodesToIncorporate,
            PotentialParseParentNode derivation)
        {
            var discriminatorLookaheadTokenPosition = derivation.CursorPosition.Value;
            var newChildren = new List<PotentialParseNode>(capacity: derivation.Children.Count);

            // retain all children before the discriminator lookahead
            for (var i = 0; i < discriminatorLookaheadTokenPosition; ++i)
            {
                newChildren.Add(derivation.Children[i]);
            }
            
            // at the discriminator lookahead, either recurse or just remove the cursor
            if (derivation.Children[discriminatorLookaheadTokenPosition] is PotentialParseParentNode parent)
            {
                newChildren.Add(this.ExpandDiscriminatorDerivation(nodesToIncorporate, parent));
            }
            else
            {
                newChildren.Add(derivation.Children[discriminatorLookaheadTokenPosition].WithoutCursor());
            }

            // beyond the discriminator lookahead, add nodes from the original discriminator rule
            for (var i = discriminatorLookaheadTokenPosition + 1; i < derivation.Children.Count; ++i)
            {
                Invariant.Require(derivation.Children[i].Symbol == nodesToIncorporate.Peek().Symbol);
                newChildren.Add(nodesToIncorporate.Dequeue());
            }

            return new PotentialParseParentNode(derivation.Rule, newChildren);
        }
 
        private static PotentialParseParentNode DefaultMarkedParseOf(RuleRemainder rule)
        {
            return new PotentialParseParentNode(rule.Rule, rule.Rule.Symbols.Select(PotentialParseNode.Create))
                .WithCursor(rule.Start);
        }
    }
}
