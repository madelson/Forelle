using Medallion.Collections;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;

namespace Forelle.Parsing.Construction.Ambiguity
{
    // TODO AMB remove expand follow stuff. ALSO, remove expandFirst stuff and move it into unification as a step that blocks alignment (cursor on non-terminal leaf)
    // we can use much of the same code we use for this on that. Finally, merge/reconcile this class with the unifier2, maybe move all to ambiguity folder

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

        public Dictionary<RuleRemainder, PotentialParseParentNode[]> GetExpandedAmbiguityContexts(IReadOnlyList<RuleRemainder> rules, Token lookaheadToken)
        {
            Guard.NotNullOrContainsNull(rules, nameof(rules));
            if (lookaheadToken == null) { throw new ArgumentNullException(nameof(lookaheadToken)); }
            
            var results = rules.ToDictionary(
                r => r,
                r => this.ExpandDiscriminatorContexts(DefaultMarkedParseOf(r), lookaheadToken, ImmutableHashSet<(Rule from, RuleRemainder to, bool isPrefix)>.Empty).ToArray()
            );
            //Invariant.Require(results.SelectMany(kvp => kvp.Value).All(n => n.CursorPosition.HasValue && !n.HasTrailingCursor()), "all expansions should contain the lookahead token");

            return results;
            //var crossJoinedContexts = CrossJoin(
            //        results.Select(kvp => kvp.Value.Select(node => (rule: kvp.Key, node)).ToArray())
            //    )
            //    .Select(c => c.ToDictionary(t => t.rule, t => t.node))
            //    .ToArray();
            //return crossJoinedContexts;
        }
        
        //private IReadOnlyList<PotentialParseNode> ExpandContexts(PotentialParseParentNode node, Token lookaheadToken)
        //{
        //    // first remove all discriminators (no-op if there are none)
        //    var nonDiscriminatorExpandedNodes = this.ExpandDiscriminatorContexts(
        //        node, 
        //        lookaheadToken, 
        //        alreadyExpandedRuleMappings: ImmutableHashSet<(Rule from, RuleRemainder to, bool isPrefix)>.Empty
        //    );
        //    // then expand
        //    var results = nonDiscriminatorExpandedNodes.SelectMany(
        //            n => this.ExpandNonDiscriminatorContexts(n, lookaheadToken, alreadyExpanded: ImmutableHashSet<(Rule rule, int index)>.Empty)
        //        )
        //        .ToArray();
        //    return results;
        //}

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
 
        //private IReadOnlyList<PotentialParseNode> ExpandNonDiscriminatorContexts(
        //    PotentialParseParentNode node,
        //    Token lookaheadToken,
        //    ImmutableHashSet<(Rule rule, int index)> alreadyExpanded)
        //{
        //    var innerContexts = this.ExpandFirstContexts(node, lookaheadToken);
        //    var outerContexts = this.ExpandFollowContexts(node, lookaheadToken, alreadyExpanded);

        //    var results = innerContexts.Append(outerContexts).ToArray();
        //    return results;
        //}

        //private IReadOnlyList<PotentialParseNode> ExpandFirstContexts(
        //    PotentialParseNode node,
        //    Token lookaheadToken)
        //{
        //    var position = node.CursorPosition.Value;

        //    if (node is PotentialParseParentNode parent)
        //    {
        //        // the expansion point is past all children, so we can't expand
        //        if (position == parent.Children.Count)
        //        {
        //            return Array.Empty<PotentialParseNode>();
        //        }

        //        // for each child starting at position and going forward until we reach
        //        // a non-nullable child, expand
        //        var expandedChildren = Enumerable.Range(position, count: parent.Children.Count - position)
        //            .TakeWhile(p => p == position || this.IsNullable(parent.Children[p - 1]))
        //            .SelectMany(
        //                p => this.ExpandFirstContexts(p == position ? parent.Children[p] : parent.Children[p].WithCursor(0), lookaheadToken),
        //                (p, expanded) => (position: p, expanded)
        //            );
        //        var results = expandedChildren.Select(t => new PotentialParseParentNode(
        //                parent.Rule,
        //                parent.Children.Select(
        //                    (ch, i) => i == t.position ? t.expanded
        //                        // all children between the initial position and the expanded position were null
        //                        : i >= position && i < t.position ? this.EmptyParseOf(ch)
        //                        : ch
        //            )))
        //            .ToArray();
        //        return results;
        //    }

        //    // leaf node
        //    Invariant.Require(position == 0);

        //    if (node.Symbol is NonTerminal nonTerminal)
        //    {
        //        // expand each rule with the token in the first
        //        var results = this._rulesByProduced[nonTerminal]
        //            .Where(r => this._firstFollowProvider.FirstOf(r.Symbols).Contains(lookaheadToken))
        //            .Select(r => DefaultMarkedParseOf(r.Skip(0)))
        //            .SelectMany(n => this.ExpandFirstContexts(n, lookaheadToken))
        //            .ToArray();
        //        return results;
        //    }

        //    // if the next symbol is the target token, then the 
        //    // only potential parse sequence is the current sequence.
        //    // Otherwise there are no valid sequences
        //    return node.Symbol == lookaheadToken
        //        ? new[] { new PotentialParseLeafNode(node.Symbol, cursorPosition: 0) }
        //        : Array.Empty<PotentialParseNode>();
        //}

        //private IReadOnlyList<PotentialParseNode> ExpandFollowContexts(
        //    PotentialParseParentNode node,
        //    Token lookaheadToken,
        //    ImmutableHashSet<(Rule rule, int index)> alreadyExpanded)
        //{
        //    var position = node.CursorPosition.Value;

        //    // if the remainder of the rule isn't nullable, we're done
        //    if (Enumerable.Range(position, count: node.Children.Count - position)
        //            .Any(i => !this.IsNullable(node.Children[i])))
        //    {
        //        return Array.Empty<PotentialParseParentNode>();
        //    }

        //    // if the lookahead can't appear, we're done
        //    if (!this._firstFollowProvider.FollowOf(node.Rule).Contains(lookaheadToken))
        //    {
        //        return Array.Empty<PotentialParseParentNode>();
        //    }

        //    // otherwise, then just find all rules that reference the current rule and expand them

        //    // we know that the current rule was parsed using the given prefix and then null productions 
        //    // for all remaining symbols (since we're looking at follow). Therefore, if we already had a
        //    // trailing cursor nothing has changed. If we didn't have a trailing cursor then we need to
        //    // move the cursor to the end, nulling out all children encountered along the way
        //    PotentialParseNode ruleParse = node.HasTrailingCursor()
        //        ? node
        //        : new PotentialParseParentNode(
        //            node.Rule,
        //            node.Children.Select(
        //                (ch, pos) => pos < position ? ch.WithoutCursor() 
        //                : pos < node.Children.Count - 1 ? this.EmptyParseOf(ch)
        //                : this.EmptyParseOf(ch).WithTrailingCursor())
        //        );

        //    // todo I'm concerned that these restrictions might make us miss lookback patterns. Basically they
        //    // would add some # of repeated null symbols between the current symbol and the lookahead token
        //    var expanded = this._nonDiscriminatorSymbolReferences.Value[node.Rule.Produced]
        //        // don't expand the same reference twice
        //        .Where(reference => !alreadyExpanded.Contains(reference))
        //        // expand a reference if it (a) changes the produced symbol OR (b) 
        //        // can have the lookahead token appear after the reference index. Expansions failing both of these criteria
        //        // fail to make "progress"; we just end up right back at this line in the same state. Because of this, such
        //        // expansions are making the context pattern less general but not more descriptive
        //        // TODO if we go back to a look-back approach we may need to handle this more robustly by having a node type that references
        //        // "any number" of expansion using rules that would otherwise fail this criterion. We may need this anyway...
        //        .Where(
        //            reference => reference.rule.Produced != node.Rule.Produced 
        //            || this._firstFollowProvider.FirstOf(reference.rule.Skip(reference.index + 1).Symbols).Contains(lookaheadToken)
        //        )
        //        .Select(reference => (
        //            node: new PotentialParseParentNode(
        //                reference.rule,
        //                reference.rule.Symbols.Select(
        //                    // remove the current cursor as long as there are following symbols (they'll get the cursor)
        //                    (s, i) => i == reference.index && i < reference.rule.Symbols.Count - 1 ? ruleParse.WithoutCursor()
        //                        : i == reference.index ? ruleParse
        //                        // place the cursor on the symbol that follows the reference point
        //                        : i == reference.index + 1 ? new PotentialParseLeafNode(s, cursorPosition: 0)
        //                        : PotentialParseNode.Create(s)
        //                )
        //            ),
        //            reference
        //        ));
        //    var result = expanded.SelectMany(t => this.ExpandNonDiscriminatorContexts(t.node, lookaheadToken, alreadyExpanded: alreadyExpanded.Add(t.reference)));
        //    return result.ToArray();
        //}

        //private bool IsNullable(PotentialParseNode node)
        //{
        //    return node is PotentialParseParentNode parent
        //        ? parent.Children.All(IsNullable) 
        //        : this._firstFollowProvider.IsNullable(node.Symbol);
        //}

        //private PotentialParseParentNode EmptyParseOf(PotentialParseNode node)
        //{
        //    return node is PotentialParseParentNode parent
        //        ? new PotentialParseParentNode(parent.Rule, parent.Children.Select(this.EmptyParseOf))
        //        : this.EmptyParseOf((NonTerminal)node.Symbol);
        //}

        //private PotentialParseParentNode EmptyParseOf(NonTerminal produced)
        //{
        //    return new PotentialParseParentNode(
        //        this._rulesByProduced[produced].Single(r => r.Symbols.Count == 0), 
        //        Enumerable.Empty<PotentialParseNode>()
        //    );
        //}

        private static PotentialParseParentNode DefaultMarkedParseOf(RuleRemainder rule)
        {
            return new PotentialParseParentNode(rule.Rule, rule.Rule.Symbols.Select(PotentialParseNode.Create))
                .WithCursor(rule.Start);
        }

        //private static List<ImmutableLinkedList<T>> CrossJoin<T>(IEnumerable<IReadOnlyCollection<T>> values)
        //{
        //    var result = new List<ImmutableLinkedList<T>>();
        //    GatherJoinedLists(ImmutableLinkedList<T>.Empty, values.ToImmutableLinkedList());
        //    return result;

        //    void GatherJoinedLists(ImmutableLinkedList<T> prefix, ImmutableLinkedList<IReadOnlyCollection<T>> suffixSources)
        //    {
        //        if (suffixSources.TryDeconstruct(out var head, out var tail))
        //        {
        //            foreach (var item in head)
        //            {
        //                var prefixWithItem = prefix.Prepend(item);
        //                if (tail.Count > 0)
        //                {
        //                    GatherJoinedLists(prefix.Prepend(item), tail);
        //                }
        //                else
        //                {
        //                    result.Add(prefixWithItem);
        //                }
        //            }
        //        }
        //    }
        //}
    }
}
