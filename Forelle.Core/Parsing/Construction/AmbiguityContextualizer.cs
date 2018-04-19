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

            var first = results.First().Value[0];
            var second = results.ElementAt(1).Value[0];
            if (this.Unify(first, second, out var unifiedFirst, out var unifiedSecond))
            {
                Console.WriteLine(unifiedFirst);
                Console.WriteLine(unifiedSecond);
            }

            return null;
            //throw new NotImplementedException();
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

        // todo n-way unify instead of 2-way
        // todo avoid building paths until we eval the node
        private bool Unify(PotentialParseNode a, PotentialParseNode b, out PotentialParseNode unifiedA, out PotentialParseNode unifiedB)
        {
            const int MaxExpansionCount = 20;

            var priorityQueue = new PriorityQueue<UnifyState>();
            var initialPathA = GetFirstPath(a);
            var initialPathB = GetFirstPath(b);
            priorityQueue.Enqueue(new UnifyState(
                pathA: initialPathA,
                pathB: initialPathB,
                expansionCount: 0,
                progressCount: 0,
                leafNodeCount: initialPathA.Last().node.EnumerateLeafNodes()
                    .Concat(initialPathB.Last().node.EnumerateLeafNodes())
                    .Count(),
                canHaveRootExpansions: true
            ));

            while (priorityQueue.Count > 0)
            {
                var currentState = priorityQueue.Dequeue();

                var symbolA = currentState.PathA.Head.node.Symbol;
                var symbolB = currentState.PathB.Head.node.Symbol;

                // if the symbols match, we can advance both paths
                if (symbolA == symbolB)
                {
                    var canAdvanceA = TryGetNext(currentState.PathA, out var nextPathA);
                    var canAdvanceB = TryGetNext(currentState.PathB, out var nextPathB);
                    if (canAdvanceA && canAdvanceB)
                    {
                        priorityQueue.Enqueue(new UnifyState(
                            pathA: nextPathA, 
                            pathB: nextPathB, 
                            expansionCount: currentState.ExpansionCount, 
                            progressCount: currentState.ProgressCount + 1,
                            leafNodeCount: currentState.LeafNodeCount,
                            canHaveRootExpansions: false
                        ));
                    }
                    else if (!canAdvanceA && !canAdvanceB)
                    {
                        // reached the end of both => return
                        unifiedA = currentState.PathA.Last().node;
                        unifiedB = currentState.PathB.Last().node;
                        // it's possible to find equivalent expansions. However, this can't
                        // be the ambiguous case so just ignore it
                        if (!AreEquivalent(unifiedA, unifiedB))
                        {
                            return true;
                        }
                    }
                }

                if (currentState.ExpansionCount < MaxExpansionCount)
                {
                    // try to make things match by expanding non-terminal symbols
                    for (var i = 0; i < 2; ++i)
                    {
                        var expandingA = i == 0;
                        if ((expandingA ? symbolA : symbolB) is NonTerminal nonTerminal)
                        {
                            foreach (var rule in this._rulesByProduced[nonTerminal])
                            {
                                if (TryGetPathAfterExpansion(
                                    expandingA ? currentState.PathA : currentState.PathB,
                                    rule,
                                    out var expandedPath))
                                {
                                    priorityQueue.Enqueue(new UnifyState(
                                        pathA: expandingA ? expandedPath : currentState.PathA,
                                        pathB: expandingA ? currentState.PathB : expandedPath,
                                        expansionCount: currentState.ExpansionCount + 1,
                                        progressCount: currentState.ProgressCount,
                                        // we are replacing a leaf with N leaves, so the leave increase is N - 1.
                                        // Since N can be 0, this can yield a net decrease
                                        leafNodeCount: currentState.LeafNodeCount + (rule.Symbols.Count - 1),
                                        canHaveRootExpansions: false
                                    ));
                                }
                            }
                        }
                    }

                    // consider outer expansions
                    if (currentState.CanHaveRootExpansions)
                    {
                        for (var i = 0; i < 2; ++i)
                        {
                            var expandingA = i == 0;
                            var rootNode = (expandingA ? currentState.PathA : currentState.PathB).Last().node;
                            foreach (var reference in this._nonDiscriminatorSymbolReferences.Value[rootNode.Symbol])
                            {
                                var newRootNode = new PotentialParseParentNode(
                                    reference.rule,
                                    reference.rule.Symbols.Select((s, index) => index == reference.index ? rootNode : new PotentialParseLeafNode(s))
                                );
                                var newPath = GetFirstPath(newRootNode);
                                var newPathA = expandingA ? newPath : currentState.PathA;
                                var newPathB = expandingA ? currentState.PathB : newPath;
                                priorityQueue.Enqueue(new UnifyState(
                                    pathA: newPathA,
                                    pathB: newPathB,
                                    expansionCount: currentState.ExpansionCount + 1,
                                    progressCount: 0,
                                    // todo if we kept leaf counts separate we could re-use some of the counting here
                                    leafNodeCount: newPathA.Last().node.EnumerateLeafNodes()
                                        .Concat(newPathB.Last().node.EnumerateLeafNodes())
                                        .Count(),
                                    canHaveRootExpansions: true
                                ));
                            }
                        }
                    }
                }
            }

            unifiedA = unifiedB = null;
            return false;
        }

        private static bool AreEquivalent(PotentialParseNode a, PotentialParseNode b)
        {
            if (a.Symbol != b.Symbol) { return false; }

            if (a is PotentialParseParentNode parentA)
            {
                if (b is PotentialParseParentNode parentB)
                {
                    if (parentA.Rule != parentB.Rule) { return false; }

                    for (var i = 0; i < parentA.Children.Count; ++i)
                    {
                        if (!AreEquivalent(parentA.Children[i], parentB.Children[i])) { return false; }
                    }

                    return true;
                }

                return false; // b is a leaf
            }

            // if we get here, a is a leaf whose symbol matches b. If b is also a leaf then they match!
            return b is PotentialParseLeafNode;
        }
        
        private sealed class UnifyState : IComparable<UnifyState>
        {
            public UnifyState(
                ImmutableLinkedList<(PotentialParseNode node, int index)> pathA,
                ImmutableLinkedList<(PotentialParseNode node, int index)> pathB,
                int expansionCount,
                int progressCount,
                int leafNodeCount,
                bool canHaveRootExpansions)
            {
                Invariant.Require(!canHaveRootExpansions || progressCount == 0);

                this.PathA = pathA;
                this.PathB = pathB;
                this.ExpansionCount = expansionCount;
                this.ProgressCount = progressCount;
                this.LeafNodeCount = leafNodeCount;
                this.CanHaveRootExpansions = canHaveRootExpansions;

                Invariant.Require(this.RemainingCount >= 0);
            }

            public ImmutableLinkedList<(PotentialParseNode node, int index)> PathA { get; }
            public ImmutableLinkedList<(PotentialParseNode node, int index)> PathB { get; }
            /// <summary>
            /// # of expansions used to transform either A or B
            /// </summary>
            public int ExpansionCount { get; }
            /// <summary>
            /// # of leaf nodes matched up between A and B before reaching the current path
            /// </summary>
            public int ProgressCount { get; }
            /// <summary>
            /// The total # of leaf nodes between the root nodes in all paths
            /// </summary>
            public int LeafNodeCount { get; }
            /// <summary>
            /// True only if the node qualifies for root expansions
            /// </summary>
            public bool CanHaveRootExpansions { get; }

            // We count progress twice because leafcount is across A and B but progress is made equally in both
            private int RemainingCount => this.LeafNodeCount - (2 * this.ProgressCount);

            int IComparable<UnifyState>.CompareTo(UnifyState that)
            {
                var expansionCountComparison = this.ExpansionCount.CompareTo(that.ExpansionCount);
                if (expansionCountComparison != 0) { return expansionCountComparison; }

                var remainingCountComparison = this.RemainingCount.CompareTo(that.RemainingCount);
                if (remainingCountComparison != 0) { return remainingCountComparison; }

                return 0;
            }
        }

        private static bool TryGetNext(ImmutableLinkedList<(PotentialParseNode node, int index)> current, out ImmutableLinkedList<(PotentialParseNode node, int index)> next)
        {
            var (head, tail) = current; // always works: a valid path cannot be empty

            if (tail.Count > 0) // if we have a parent...
            {
                // ...try expanding later children
                var parent = (PotentialParseParentNode)tail.Head.node;
                for (var i = head.index + 1; i < parent.Children.Count; ++i)
                {
                    if (TryExpandFirstPath(tail.Prepend((node: parent.Children[i], index: i)), out next))
                    {
                        return true;
                    }
                }

                // otherwise recurse on the parent's parent
                return TryGetNext(tail, out next);
            }

            next = default;
            return false;
        }

        private static ImmutableLinkedList<(PotentialParseNode node, int index)> GetFirstPath(PotentialParseNode node)
        {
            Invariant.Require(
                TryExpandFirstPath(ImmutableLinkedList.Create((node, index: 0)), out var result),
                "Empty parse node"
            );

            return result;
        }

        private static bool TryExpandFirstPath(
            ImmutableLinkedList<(PotentialParseNode node, int index)> basePath,
            out ImmutableLinkedList<(PotentialParseNode node, int index)> result)
        {
            var (head, tail) = basePath;
            if (head.node is PotentialParseParentNode parent)
            {
                for (var i = 0; i < parent.Children.Count; ++i)
                {
                    if (TryExpandFirstPath(basePath.Prepend((node: parent.Children[i], index: i)), out result))
                    {
                        return true;
                    }
                }

                result = default;
                return false;
            }

            result = basePath;
            return true;
        }

        private static bool TryGetPathAfterExpansion(
            ImmutableLinkedList<(PotentialParseNode node, int index)> path,
            Rule rule,
            out ImmutableLinkedList<(PotentialParseNode node, int index)> nextPath)
        {
            Invariant.Require(path.Head.node is PotentialParseLeafNode);

            // build a new path replacing the leaft with a default parse of the rule
            var newPath = ReplaceLeafNode(path, DefaultParseOf(rule));

            // if the rule has children, then we will return a path pointing to the first child
            if (rule.Symbols.Count > 0)
            {
                nextPath = newPath.Prepend((node: ((PotentialParseParentNode)newPath.Head.node).Children[0], index: 0));
                return true;
            }

            // if the rule does not have children, then we need to search for the next valid path
            return TryGetNext(newPath, out nextPath);
        }

        private static ImmutableLinkedList<(PotentialParseNode node, int index)> ReplaceLeafNode(
            ImmutableLinkedList<(PotentialParseNode node, int index)> path,
            PotentialParseNode replacement)
        {
            var (head, tail) = path;
            Invariant.Require(head.node.Symbol == replacement.Symbol);

            // base case
            if (tail.Count == 0)
            {
                return ImmutableLinkedList.Create((node: replacement, index: 0));
            }

            // recursive case: compute an expanded parent and expand tail as that
            var parent = (PotentialParseParentNode)tail.Head.node;
            var expandedParent = new PotentialParseParentNode(
                parent.Rule,
                parent.Children.Select((ch, index) => index == head.index ? replacement : ch)
            );
            return ReplaceLeafNode(tail, expandedParent).Prepend((node: replacement, head.index)); 
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
