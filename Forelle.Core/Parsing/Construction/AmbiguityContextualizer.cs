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

            var results = rules.ToDictionary(
                r => r,
                r => this.ExpandContexts(DefaultParseOf(r.Rule), lookaheadToken, r.Start).ToArray()
            );
            
            throw new NotImplementedException();
        }
        
        private IReadOnlyList<PotentialParseNode> ExpandContexts(PotentialParseParentNode node, Token lookaheadToken, int position)
        {
            var results = node.Symbol.SyntheticInfo is DiscriminatorSymbolInfo
                ? this.ExpandDiscriminatorContexts(node, lookaheadToken, position)
                : this.ExpandNonDiscriminatorContexts(node, lookaheadToken, position, alreadyExpanded: ImmutableHashSet<Rule>.Empty);
            return results;
        }

        private IReadOnlyList<PotentialParseNode> ExpandDiscriminatorContexts(PotentialParseParentNode discriminatorNode, Token lookaheadToken, int position)
        {
            Invariant.Require(discriminatorNode.Symbol.SyntheticInfo is DiscriminatorSymbolInfo);

            var results = this._discriminatorContexts[discriminatorNode.Rule.Produced]
                .SelectMany(c => this.ExpandDiscriminatorContext(discriminatorNode, c, lookaheadToken, position))
                .ToArray();
            return results;
        }

        private IReadOnlyList<PotentialParseNode> ExpandDiscriminatorContext(
            PotentialParseParentNode discriminatorNode, 
            DiscriminatorContext discriminatorContext,
            Token lookaheadToken,
            int position)
        {
            var ruleMapping = discriminatorContext.RuleMappings.Single(t => t.DiscriminatorRule == discriminatorNode.Rule);
            var mappedRule = ruleMapping.MappedRule;

            if (discriminatorContext is PrefixDiscriminatorContext)
            {
                var adjustedPosition = mappedRule.Start - (discriminatorNode.Children.Count - position);
                var adjustedNode = new PotentialParseParentNode(
                    mappedRule.Rule,
                    discriminatorNode.Children.Concat(mappedRule.Symbols.Select(s => new PotentialParseLeafNode(s)))
                );

                // for a prefix, just expand
                var prefixResult = this.ExpandDiscriminatorContexts(
                    adjustedNode,
                    lookaheadToken,
                    adjustedPosition
                );
                return prefixResult;
            }

            // otherwise, expand using the expansion path and then do a normal expansion
            var expansionPaths = ((PostTokenSuffixDiscriminatorContext.RuleMapping)ruleMapping).ExpansionPaths;
            var expanded = expansionPaths.Select(p => (
                node: this.ExpandDiscriminatorExpansionPath(p.ToImmutableLinkedList(), discriminatorNode.Children.ToImmutableLinkedList()),
                position: p[0].Start + 1 + position
            ));
            var results = expanded.SelectMany(t => this.ExpandContexts(t.node, lookaheadToken, t.position))
                .ToArray();
            return results;
        }

        private PotentialParseParentNode ExpandDiscriminatorExpansionPath(
            ImmutableLinkedList<RuleRemainder> expansionPath,
            // todo probably should be IReadOnlyList based on usage
            ImmutableLinkedList<PotentialParseNode> existingParseNodes)
        {
            var (head, rest) = expansionPath;

            // the number of nodes we expect to match is the number that follow the expansion of
            // the head node
            var existingParseMatchCount = Math.Max(head.Rule.Symbols.Count - (head.Start + 1), 0);
            Invariant.Require(existingParseMatchCount <= existingParseNodes.Count);
            
            var result = new PotentialParseParentNode(
                head.Rule,
                head.Rule.Symbols.Select(
                    // everything before start parsed as empty (since we were peeling off a token)
                    (s, i) => i < head.Start ? this.EmptyParseOf((NonTerminal)s)
                        // at start itself we expand using the rest of the path
                        // TODO what do we do here if rest.Count == 0 and i == head.Start???
                        : i == head.Start && rest.Count > 0 ? this.ExpandDiscriminatorExpansionPath(rest, existingParseNodes.SubList(0, existingParseNodes.Count - existingParseMatchCount))
                        // for suffix symbols use discriminatorNode if we have it
                        : i > head.Start ? existingParseNodes.Skip(existingParseNodes.Count - existingParseMatchCount).ElementAt(i - (head.Start + 1))
                        : (PotentialParseNode)new PotentialParseLeafNode(s)
                )
            );
            return result;
        }
 
        private IReadOnlyList<PotentialParseNode> ExpandNonDiscriminatorContexts(
            PotentialParseParentNode node,
            Token lookaheadToken,
            int position,
            ImmutableHashSet<Rule> alreadyExpanded)
        {
            var innerContexts = this.ExpandFirstContexts(node, lookaheadToken, position);
            var outerContexts = this.ExpandFollowContexts(node, lookaheadToken, position, alreadyExpanded);

            var results = innerContexts.Append(outerContexts).ToArray();
            return results;
        }

        private IReadOnlyList<PotentialParseNode> ExpandFirstContexts(
            PotentialParseNode node,
            Token lookaheadToken,
            int position)
        {
            if (node is PotentialParseParentNode parent)
            {
                Invariant.Require(0 <= position && position <= parent.Children.Count);

                // the expansion point is past all children, so we can't expand
                if (position == parent.Children.Count)
                {
                    return Array.Empty<PotentialParseNode>();
                }
                
                // for each child starting at position and going forward until we reach
                // a non-nullable child, expand
                var expandedChildren = Enumerable.Range(position, count: parent.Children.Count - position)
                    .TakeWhile(p => p == position || this.IsNullable(parent.Children[p - 1]))
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
                    )))
                    .ToArray();
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
                    .SelectMany(n => this.ExpandFirstContexts(n, lookaheadToken, position: 0))
                    .ToArray();
                return results;
            }
            
            // if the next symbol is the target token, then the 
            // only potential parse sequence is the current sequence.
            // Otherwise there are no valid sequences
            return node.Symbol == lookaheadToken
                ? new[] { node }
                : Array.Empty<PotentialParseNode>();
        }

        private IReadOnlyList<PotentialParseNode> ExpandFollowContexts(
            PotentialParseParentNode node,
            Token lookaheadToken,
            int position,
            ImmutableHashSet<Rule> alreadyExpanded) // todo should this be (Rule, index)?
        {
            Invariant.Require(0 <= position && position <= node.Rule.Symbols.Count);

            // if the remainder of the rule isn't nullable, we're done
            if (Enumerable.Range(position, count: node.Children.Count - position)
                    .Any(i => !this.IsNullable(node.Children[i])))
            {
                return Array.Empty<PotentialParseParentNode>();
            }

            // if the lookahead can't appear, we're done
            if (!this._firstFollowProvider.FollowOf(node.Rule).Contains(lookaheadToken))
            {
                return Array.Empty<PotentialParseParentNode>();
            }

            // otherwise, then just find all rules that reference the current rule and expand them

            // we know that the current rule was parsed using the given prefix and then null productions for all remaining symbols (since we're looking at follow)
            PotentialParseNode ruleParse = new PotentialParseParentNode(
                node.Rule,
                node.Children.Select((ch, pos) => pos < position ? ch : this.EmptyParseOf(ch))
            );

            var expanded = this._nonDiscriminatorSymbolReferences.Value[node.Rule.Produced]
                // don't expand the same reference twice
                .Where(reference => !alreadyExpanded.Contains(reference.rule))
                // don't expand a reference is it (a) does not change the produced symbol and (b) 
                // cannot have the lookahead token appear after the reference index. The reason is that this
                // expansion won't get us anywhere new; we'll end up right back at this line with the same produced
                // symbol
                .Where(
                    reference => reference.rule.Produced != node.Rule.Produced 
                    || this._firstFollowProvider.FirstOf(reference.rule.Skip(reference.index + 1).Symbols).Contains(lookaheadToken)
                )
                .Select(reference => (
                    node: new PotentialParseParentNode(
                        reference.rule,
                        reference.rule.Symbols.Select((s, i) => i == reference.index ? ruleParse : new PotentialParseLeafNode(s))
                    ),
                    position: reference.index + 1
                ));
            var result = expanded.SelectMany(t => this.ExpandNonDiscriminatorContexts(t.node, lookaheadToken, t.position, alreadyExpanded: alreadyExpanded.Add(t.node.Rule)));
            return result.ToArray();
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
                this.Symbol = symbol ?? throw new ArgumentNullException(nameof(symbol));
            }

            public Symbol Symbol { get; }

            public abstract IEnumerable<PotentialParseLeafNode> EnumerateLeafNodes();

            protected static string ToString(Symbol symbol) => symbol.Name.Any(char.IsWhiteSpace)
                || symbol.Name.IndexOf('(') >= 0
                || symbol.Name.IndexOf(')') >= 0
                ? $"\"{symbol.Name}\""
                : symbol.Name;
        }
        
        internal sealed class PotentialParseLeafNode : PotentialParseNode
        {
            public PotentialParseLeafNode(Symbol symbol)
                : base(symbol)
            {
            }

            public override IEnumerable<PotentialParseLeafNode> EnumerateLeafNodes() { yield return this; }

            public override string ToString() => ToString(this.Symbol);
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

            public override string ToString() => $"{ToString(this.Symbol)}({string.Join(" ", this.Children)})";
        }
    }
}
