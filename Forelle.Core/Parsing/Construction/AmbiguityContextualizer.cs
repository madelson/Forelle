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

            var results = rules.Select(r => (node: DefaultParseOf(r.Rule), position: r.Start))
                .SelectMany(t => this.ExpandNonDiscriminatorContexts(t.node, lookaheadToken, t.position))
                .ToArray();
            
            throw new NotImplementedException();
        }
        
        private IEnumerable<PotentialParseNode> ExpandContexts(RuleRemainder rule, Token lookaheadToken)
        {
            return rule.Produced.SyntheticInfo is DiscriminatorSymbolInfo
                ? this.ExpandDiscriminatorContexts(rule, lookaheadToken)
                : this.ExpandNonDiscriminatorContexts(DefaultParseOf(rule.Rule), lookaheadToken, rule.Start);
        }

        private IEnumerable<PotentialParseNode> ExpandDiscriminatorContexts(RuleRemainder discriminatorRule, Token lookaheadToken)
        {
            return this._discriminatorContexts[discriminatorRule.Produced]
                .SelectMany(c => this.ExpandDiscriminatorContext(discriminatorRule, c, lookaheadToken));
        }

        private IEnumerable<PotentialParseNode> ExpandDiscriminatorContext(
            RuleRemainder discriminatorRule, 
            DiscriminatorContext discriminatorContext,
            Token lookaheadToken)
        {
            var ruleMapping = discriminatorContext.RuleMappings.Single(t => t.DiscriminatorRule == discriminatorRule.Rule);
            var mappedRule = ruleMapping.MappedRule;

            if (discriminatorContext is PrefixDiscriminatorContext)
            {
                return this.ExpandDiscriminatorContexts(
                    mappedRule.Rule.Skip(mappedRule.Start - discriminatorRule.Symbols.Count),
                    lookaheadToken
                );
            }

            var expansionPaths = ((PostTokenSuffixDiscriminatorContext.RuleMapping)ruleMapping).ExpansionPaths;
            throw new NotImplementedException();
        }

        private IEnumerable<PotentialParseNode> ExpandDiscriminatorContext(
            ImmutableLinkedList<RuleRemainder> expansionPath,
            Token discriminatorLookaheadToken,
            Token lookaheadToken,
            int? position)
        {
            throw new NotImplementedException();

            var (head, rest) = expansionPath;

            if (rest.Count == 0)
            {
                // this was the last expansion, which means that the first symbol of head is 
            }

            if (position != head.Start)
            {

            }
        }
 
        private IEnumerable<PotentialParseNode> ExpandNonDiscriminatorContexts(
            PotentialParseParentNode node,
            Token lookaheadToken,
            int position)
        {
            var innerContexts = this.ExpandFirstContexts(node, lookaheadToken, position);
            var outerContexts = this.ExpandFollowContexts(node, lookaheadToken, position);

            return innerContexts.Append(outerContexts);
        }

        private IEnumerable<PotentialParseNode> ExpandFirstContexts(
            PotentialParseNode node,
            Token lookaheadToken,
            int position)
        {
            if (node is PotentialParseParentNode parent)
            {
                Invariant.Require(0 <= position && position < parent.Children.Count);

                // for each child starting at position and going forward until we reach
                // a non-nullable child, expand
                var expandedChildren = Enumerable.Range(position, count: parent.Children.Count - position)
                    .TakeWhile(p => p == 0 || this.IsNullable(parent.Children[p - 1]))
                    .SelectMany(
                        p => this.ExpandFirstContexts(parent.Children[p], lookaheadToken, position: 0),
                        (p, expanded) => (position: p, expanded)
                    );
                var results = expandedChildren.Select(t => new PotentialParseParentNode(
                    parent.Rule,
                    parent.Children.Select(
                        (ch, i) => i == t.position ? t.expanded
                            // all children between the initial position and the expanded position were null
                            : i >= position && i < t.position ? this.EmptyParseOf(ch)
                            : ch
                )));
                return results;
            }

            // leaf node
            Invariant.Require(position == 0);

            if (node.Symbol is NonTerminal nonTerminal)
            {
                // expand each rule with the token in the first
                var results = this._rulesByProduced[nonTerminal]
                    .Where(r => this._firstFollowProvider.FirstOf(r.Symbols).Contains(lookaheadToken))
                    .Select(DefaultParseOf)
                    .SelectMany(n => this.ExpandFirstContexts(n, lookaheadToken, position: 0));
                return results;
            }
            
            // if the next symbol is the target token, then the 
            // only potential parse sequence is the current sequence.
            // Otherwise there are no valid sequences
            return node.Symbol == lookaheadToken
                ? new[] { node }
                : Enumerable.Empty<PotentialParseNode>();
        }

        private IEnumerable<PotentialParseNode> ExpandFollowContexts(
            PotentialParseParentNode node,
            Token lookaheadToken,
            int position)
        {
            Invariant.Require(0 <= position && position <= node.Rule.Symbols.Count);

            // if the remainder of the rule isn't nullable, we're done
            if (Enumerable.Range(position, count: node.Children.Count - position)
                    .Any(i => !this.IsNullable(node.Children[i])))
            {
                return Enumerable.Empty<PotentialParseParentNode>();
            }

            // if the lookahead can't appear, we're done
            if (!this._firstFollowProvider.FollowOf(node.Rule).Contains(lookaheadToken))
            {
                return Enumerable.Empty<PotentialParseParentNode>();
            }

            // otherwise, then just find all rules that reference the current rule and expand them

            // we know that the current rule was parsed using the given prefix and then null productions for all remaining symbols (since we're looking at follow)
            PotentialParseNode ruleParse = new PotentialParseParentNode(
                node.Rule,
                node.Children.Select((ch, pos) => pos < position ? ch : this.EmptyParseOf(ch))
            );

            var expanded = this._nonDiscriminatorSymbolReferences.Value[node.Rule.Produced]
                .Select(reference => (
                    node: new PotentialParseParentNode(
                        reference.rule,
                        reference.rule.Symbols.Select((s, i) => i == reference.index ? ruleParse : new PotentialParseLeafNode(s))
                    ),
                    position: reference.index + 1
                ));
            return expanded.SelectMany(t => this.ExpandNonDiscriminatorContexts(t.node, lookaheadToken, t.position));
        }
        
        private bool IsNullable(PotentialParseNode node)
        {
            return node is PotentialParseParentNode parent
                ? parent.Children.All(IsNullable) 
                : this._firstFollowProvider.IsNullable(node.Symbol);
        }

        private PotentialParseParentNode EmptyParseOf(PotentialParseNode node)
        {
            return node is PotentialParseParentNode parent
                ? new PotentialParseParentNode(parent.Rule, parent.Children.Select(this.EmptyParseOf))
                : this.EmptyParseOf((NonTerminal)node.Symbol);
        }

        private PotentialParseParentNode EmptyParseOf(NonTerminal produced)
        {
            return new PotentialParseParentNode(
                this._rulesByProduced[produced].Single(r => r.Symbols.Count == 0), 
                Enumerable.Empty<PotentialParseNode>()
            );
        }

        private static PotentialParseParentNode DefaultParseOf(Rule rule)
        {
            return new PotentialParseParentNode(
                rule,
                rule.Symbols.Select(s => new PotentialParseLeafNode(s))
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

                if (!this.Rule.Symbols.SequenceEqual(this.Children.Select(c => c.Symbol))) { throw new ArgumentException("rule symbols and child symbols must match"); }
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
