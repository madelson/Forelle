using Medallion.Collections;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;

namespace Forelle.Parsing.Construction
{
    // idea: prefix only gets used for ambiguity resolution. Rather than cache by prefix and therefore repeat work,
    // we could instead cache only by node. When we retrieve a node from the cache, we can note the prefix that goes with it.
    // When we reach an ambiguity point, we can immediately create a stub node. Later, can we flow prefixes down to all stub nodes,
    // and then replace the stub nodes with the appropriate ambiguity resolutions? Flowing down would be looking for all paths from
    // the top level, starting with the top-level prefix and adding on implied prefixes as we go (e. g. a prefix-parse node adds an implied prefix)

    // alternatively, can we just infer the prefix(es) from the set of rules we are choosing from? We can expand out partial rules, and inline
    // discriminators. 

    // In some cases we can even use the lookahead token to further expand backwards to give more context. E. g. for dangling else,
    // we'll be choosing between "if E then E" and "if E then E else E". We realize that else is only in the follow of the first rule in the instance
    // where there was a surrounding if, so the full context becomes if E then if E then E else E

    /// <summary>
    /// Implements the core Forelle parser generation algorithm
    /// </summary>
    internal class ParserGenerator
    {
        private readonly Dictionary<NonTerminal, IReadOnlyList<Rule>> _rulesByProduced;
        private readonly DiscriminatorFirstFollowProviderBuilder _firstFollow;
        private readonly DiscriminatorHelper _discriminatorHelper;

        private readonly Queue<NodeContext> _generatorQueue = new Queue<NodeContext>();
        private readonly List<string> _errors = new List<string>();

        private readonly Dictionary<NodeContext, ParserNode> _nodesByContext = new Dictionary<NodeContext, ParserNode>();
        private readonly Dictionary<ReferenceNode, ReferenceContext> _referenceNodeContexts = new Dictionary<ReferenceNode, ReferenceContext>();

        private ParserGenerator(IReadOnlyList<Rule> rules)
        {
            this._rulesByProduced = rules.GroupBy(r => r.Produced)
                .ToDictionary(g => g.Key, g => (IReadOnlyList<Rule>)g.ToList());

            var baseFirstFollow = FirstFollowCalculator.Create(rules);
            this._firstFollow = new DiscriminatorFirstFollowProviderBuilder(baseFirstFollow);

            this._discriminatorHelper = new DiscriminatorHelper(this._rulesByProduced, this._firstFollow);
        }

        public static (Dictionary<StartSymbolInfo, ParserNode> nodes, List<string> errors) CreateParser(IReadOnlyList<Rule> rules)
        {
            var generator = new ParserGenerator(rules);
            var nodes = generator.Generate();

            return (nodes: nodes, errors: generator._errors);
        }

        private Dictionary<StartSymbolInfo, ParserNode> Generate()
        {
            // pre-populate the generator queue with contexts for each start symbol
            var startSymbolContexts = this._rulesByProduced.Select(kvp => (startInfo: kvp.Key.SyntheticInfo as StartSymbolInfo, rules: kvp.Value))
                .Where(t => t.startInfo != null)
                .ToDictionary(t => t.startInfo, t => new NodeContext(t.rules.Select(r => new RuleRemainder(r, start: 0))));
            foreach (var context in startSymbolContexts.Values)
            {
                this._generatorQueue.Enqueue(context);
            }

            // continue generating nodes until the queue is empty
            while (this._generatorQueue.Count > 0)
            {
                var nextContext = this._generatorQueue.Dequeue();
                if (!this._nodesByContext.ContainsKey(nextContext))
                {
                    this._nodesByContext.Add(nextContext, this.CreateParserNode(nextContext));
                }
            }

            // link up all dangling reference nodes
            this.ConnectReferences();

            return startSymbolContexts.ToDictionary(kvp => kvp.Key, kvp => this._nodesByContext[kvp.Value]);
        }

        private void ConnectReferences()
        {
            // note: since no references are filled in yet, we can't possibly have circular paths. Therefore
            // the depth-first traversal WILL terminate
            var allReferences = this._nodesByContext.Values
                .SelectMany(root => Traverse.DepthFirst(root, n => n is ReferenceNode ? Enumerable.Empty<ParserNode>() : n.ChildNodes))
                .OfType<ReferenceNode>()
                .Distinct()
                .ToArray();

            foreach (var reference in allReferences)
            {
                var referenceContext = this._referenceNodeContexts[reference];
                reference.SetValue(this._nodesByContext[referenceContext.NodeContext.Value]);
            }
        }

        private ParserNode CreateParserNode(NodeContext context)
        {
            return context.Lookahead == null 
                ? this.CreateLL1OrNonLL1ReferenceParserNode(context.Rules)
                : this.CreateNonLL1ParserNode(context.Lookahead, context.Rules);
        }

        private ParserNode CreateLL1OrNonLL1ReferenceParserNode(IReadOnlyList<RuleRemainder> rules)
        {
            // if we only have one rule, we just parse that
            if (rules.Count == 1)
            {
                return this.ReferenceNodeFor(rules.Single());
            }

            // next, see what we can do with LL(1) single-token lookahead
            var tokenLookaheadTable = rules.SelectMany(r => this._firstFollow.NextOf(r), (r, t) => (rule: r, token: t))
                .GroupBy(t => t.token, t => t.rule)
                .ToDictionary(g => g.Key, g => g.ToArray());

            // if there is only one entry in the table, just create a non-LL(1) node for that entry.
            // We know that this will be non-LL(1) because we already checked for the single-rule case above
            if (tokenLookaheadTable.Count == 1)
            {
                // typically, we should never return a reference node as a direct result. However, in this case it's ok because
                // The reference is for a narrower context than the current context
                return this.ReferenceNodeFor(new NodeContext(tokenLookaheadTable.Single().Value, tokenLookaheadTable.Single().Key));
            }

            // else, create a token lookahead node mapping from the table
            return new TokenLookaheadNode(
                tokenLookaheadTable.ToDictionary(
                    kvp => kvp.Key,
                    kvp => kvp.Value.Length == 1
                        ? this.ReferenceNodeFor(kvp.Value.Single())
                        : this.ReferenceNodeFor(new NodeContext(kvp.Value, kvp.Key))
                )
            );
        }

        private ParserNode CreateNonLL1ParserNode(Token lookaheadToken, IReadOnlyList<RuleRemainder> rules)
        {
            return this.TryCreatePrefixParserNode(lookaheadToken, rules)
                ?? this.TryCreateDiscriminatorPrefixParserNode(lookaheadToken, rules)
                ?? this.TryCreateDiscriminatorLookaheadParserNode(lookaheadToken, rules)
                ?? throw new NotImplementedException();
        }

        private ParserNode TryCreatePrefixParserNode(Token lookaheadToken, IReadOnlyList<RuleRemainder> rules)
        {
            // see if we can find a common prefix among all rules. If we can, we'll just parse the prefix
            // and then follow up by parsing the remainder
            var prefixLength = Enumerable.Range(0, count: rules.Min(r => r.Symbols.Count))
                .TakeWhile(i => rules.Skip(1).All(r => r.Symbols[i] == rules[0].Symbols[i]))
                .Select(i => i + 1)
                .LastOrDefault();

            if (prefixLength > 0
                // ignore prefixes which are all tokens. These are easily handled by discriminator-based lookahead, and it seems
                // that leaving them out can lead to more discriminator re-use and more compact discriminator grammars in general
                // See the grammar in TestExpressionVsStatementListConflict for an example
                && !rules[0].Symbols.Take(prefixLength).All(r => r is Token))
            {
                var prefix = rules[0].Symbols.Take(prefixLength)
                    .Select(s => s is Token t ? new TokenOrParserNode(t) : new TokenOrParserNode(this.ReferenceNodeFor((NonTerminal)s)));

                var suffixNode = this.ReferenceNodeFor(new NodeContext(rules.Select(r => new RuleRemainder(r.Rule, r.Start + prefixLength))));
                return new ParsePrefixSymbolsNode(prefix: prefix, suffixNode: suffixNode);
            }

            return null;
        }

        private static readonly IComparer<DiscriminatorHelper.DiscriminatorPrefixSearchResult> PrefixSearchResultComparer =
            Comparers.Create((DiscriminatorHelper.DiscriminatorPrefixSearchResult r) => r.IsFollowCompatible)
                .ThenBy(Comparers.Create((DiscriminatorHelper.DiscriminatorPrefixSearchResult r) => r.RulesToDiscriminatorRuleMapping.Values.Sum(v => v.Symbols.Count)))
                // finally, break ties by preferring root discriminators. This is useful because it keeps the follow set from growing more than
                // is needed, thus minimizing potential issues. However, this may lead to more total child discriminators being produced
                .ThenBy(Comparers.Create((DiscriminatorHelper.DiscriminatorPrefixSearchResult r) => ((DiscriminatorSymbolInfo)r.Discriminator.SyntheticInfo).ParentDiscriminator != null));

        private ParserNode TryCreateDiscriminatorPrefixParserNode(Token lookaheadToken, IReadOnlyList<RuleRemainder> rules)
        {
            // if we are producing a discriminator, see if an existing discriminator is a prefix. This
            // lets us handle recursion within the lookahead grammar

            var produced = rules.Only(r => r.Produced);
            if (!(produced.SyntheticInfo is DiscriminatorSymbolInfo)) { return null; }
            
            var matches = this._discriminatorHelper.FindPrefixDiscriminators(rules, lookaheadToken);
            var bestMatch = matches.Where(r => !r.IsExactMatch || r.IsFollowCompatible)
                .MaxBy(r => r, PrefixSearchResultComparer);
            if (bestMatch == null) { return null; }

            if (bestMatch.IsFollowCompatible)
            {
                return new MapResultNode(
                    this.ReferenceNodeFor(bestMatch.Discriminator),
                    bestMatch.RulesToDiscriminatorRuleMapping
                        .GroupBy(kvp => kvp.Value, kvp => new RuleRemainder(kvp.Key.Rule, start: kvp.Key.Start + kvp.Value.Symbols.Count))
                        .ToDictionary(
                            g => g.Key,
                            g => this.ReferenceNodeFor(new NodeContext(g))
                        )
                );
            }

            // otherwise, we need to create a new discriminator that is follow-compatible
            var newDiscriminator = NonTerminal.CreateSynthetic(
                bestMatch.Discriminator.Name.TrimStart(Symbol.SyntheticMarker)
                    + new string('\'', count: this._rulesByProduced.Keys.Count(s => s.SyntheticInfo is DiscriminatorSymbolInfo dsi && dsi.ParentDiscriminator == bestMatch.Discriminator) + 1),
                new DiscriminatorSymbolInfo(parentDiscriminator: bestMatch.Discriminator)
            );
            var newDiscriminatorRulesToRulesMapping = bestMatch.RulesToDiscriminatorRuleMapping
                .GroupBy(kvp => kvp.Value, kvp => kvp.Key)
                .Select(g => (
                    newDiscriminatorRule: new Rule(newDiscriminator, g.Key.Symbols, g.Key.ExtendedInfo),
                    remainderMappedRules: g.Select(r => new RuleRemainder(r.Rule, start: r.Start + g.Key.Symbols.Count))
                        .ToArray()
                ))
                .ToArray();
            var rulesToFollowSets = newDiscriminatorRulesToRulesMapping
                .ToDictionary(
                    t => t.newDiscriminatorRule,
                    // todo could use the tighter follow calculation here that we use for IsFollowCompatible that accounts for the
                    // lookahead token
                    t => t.remainderMappedRules.Select(r => this._firstFollow.NextOf(r))
                        .Aggregate((s1, s2) => s1.Union(s2))
                );
            this._rulesByProduced.Add(newDiscriminator, newDiscriminatorRulesToRulesMapping.Select(t => t.newDiscriminatorRule).ToArray());
            this._firstFollow.Add(rulesToFollowSets);

            return new MapResultNode(
                this.ReferenceNodeFor(newDiscriminator),
                newDiscriminatorRulesToRulesMapping.ToDictionary(
                    t => t.newDiscriminatorRule,
                    t => this.ReferenceNodeFor(new NodeContext(t.remainderMappedRules))
                )
            );
        }
        
        private ParserNode TryCreateDiscriminatorLookaheadParserNode(Token lookaheadToken, IReadOnlyList<RuleRemainder> rules)
        {
            // build a mapping of what we would see after passing by lookaheadToken to the rule that that suffix would indicate
            var suffixToRuleMapping = this.TryBuildSuffixToRuleMapping(lookaheadToken, rules);
            if (suffixToRuleMapping == null) { return null; }

            // TODO in the old code we looked for an equivalent existing discriminator here...

            // construct a discriminator symbol

            var discriminator = NonTerminal.CreateSynthetic(
                "T" + this._rulesByProduced.Keys.Count(k => k.SyntheticInfo is DiscriminatorSymbolInfo),
                new DiscriminatorSymbolInfo() // TODO what info goes here? suffixToRuleMapping?
            );
            var rulesAndFollowSets = suffixToRuleMapping.ToDictionary(
                kvp => new Rule(discriminator, kvp.Key, kvp.Value.Rule.ExtendedInfo), 
                kvp => this._firstFollow.FollowOf(kvp.Value.Rule)
            );

            this._rulesByProduced.Add(discriminator, rulesAndFollowSets.Keys.ToArray());
            this._firstFollow.Add(rulesAndFollowSets);
            this._generatorQueue.Enqueue(new NodeContext(rulesAndFollowSets.Keys.Select(r => new RuleRemainder(r, start: 0))));

            return new GrammarLookaheadNode(
                token: lookaheadToken,
                discriminatorParse: this.ReferenceNodeFor(discriminator),
                mapping: rulesAndFollowSets.Keys
                    .Select(r => (fromRule: r, toRule: suffixToRuleMapping[r.Symbols]))
                    .ToDictionary(t => t.fromRule, t => (t.toRule.Rule, this.ReferenceNodeFor(t.toRule)))
            );
        }

        private IReadOnlyDictionary<IReadOnlyList<Symbol>, RuleRemainder> TryBuildSuffixToRuleMapping(Token lookaheadToken, IReadOnlyList<RuleRemainder> rules)
        {
            var suffixToRuleMapping = new Dictionary<IReadOnlyList<Symbol>, RuleRemainder>(EqualityComparers.GetSequenceComparer<Symbol>());
            foreach (var rule in rules)
            {
                var suffixes = this._discriminatorHelper.TryGatherPostTokenSuffixes(lookaheadToken, rule);
                if (suffixes == null)
                {
                    return null;
                }

                foreach (var suffix in suffixes)
                {
                    if (suffixToRuleMapping.ContainsKey(suffix))
                    {
                        // in theory we could handle this case if the two rules with the same suffix
                        // have disjoint follow sets. However, this seems like it would come up rarely
                        return null;
                    }
                    suffixToRuleMapping.Add(suffix, rule);
                }
            }
            return suffixToRuleMapping;
        }

        /// <summary>
        /// Represents a context in which a <see cref="ParserNode"/> is entered. A context is composed
        /// of a set of <see cref="RuleRemainder"/>s we are choosing between and possibly a specific
        /// lookahead <see cref="Token"/>
        /// </summary>
        private struct NodeContext : IEquatable<NodeContext>
        {
            private static IEqualityComparer<IEnumerable<RuleRemainder>> RulesComparer = EqualityComparers.GetSequenceComparer<RuleRemainder>();

            public NodeContext(IEnumerable<RuleRemainder> rules, Token lookahead = null)
            {
                this.Rules = (rules ?? throw new ArgumentNullException(nameof(rules))).ToArray();
                this.Lookahead = lookahead;
            }

            public IReadOnlyList<RuleRemainder> Rules { get; }
            public Token Lookahead { get; }

            public override bool Equals(object thatObj) => thatObj is NodeContext that && this.Equals(that);

            public bool Equals(NodeContext that)
            {
                return this.Rules.SequenceEqual(that.Rules)
                    && this.Lookahead == that.Lookahead;
            }

            public override int GetHashCode() => (RulesComparer.GetHashCode(this.Rules), this.Lookahead?.GetHashCode()).GetHashCode();
        }

        private ParserNode ReferenceNodeFor(NodeContext nodeContext)
        {
            if (this._nodesByContext.TryGetValue(nodeContext, out var existing))
            {
                return existing;
            }

            this._generatorQueue.Enqueue(nodeContext); // will have to be computed

            // return a reference to the future computation
            var reference = new ReferenceNode();
            this._referenceNodeContexts.Add(reference, new ReferenceContext(nodeContext));
            return reference;
        }

        private ParserNode ReferenceNodeFor(NonTerminal nonTerminal)
        {
            var nodeContext = new NodeContext(this._rulesByProduced[nonTerminal].Select(r => new RuleRemainder(r, start: 0)));
            return this.ReferenceNodeFor(nodeContext);
        }

        private ParserNode ReferenceNodeFor(RuleRemainder rule)
        {
            return new ParseRuleNode(
                rule,
                rule.Symbols.OfType<NonTerminal>().Select(this.ReferenceNodeFor)
            );
        }

        private sealed class ReferenceContext
        {
            public ReferenceContext(NodeContext nodeContext)
            {
                this.NodeContext = nodeContext;
            }

            public NodeContext? NodeContext { get; }
        }

        private string DebugGrammar => string.Join(
            Environment.NewLine + Environment.NewLine,
            this._rulesByProduced.Select(kvp => $"{kvp.Key}:{Environment.NewLine}" + string.Join(Environment.NewLine, kvp.Value.Select(r => "\t" + r)))
        );
    }
}
