using Medallion.Collections;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Forelle.Parsing.Construction
{
    // idea: we currently have 5 ways to parse:
    // single rule parse
    // token switch parse
    // common prefix parse
    // discriminator lookahead switch
    // discriminator prefix switch
    //
    // To deal with cases like in DiscriminatorExpansionEdgeCasesTest, we might need
    // to introduce a new approach: custom discriminator prefixes that might not be differentiable (similar to common prefixes)
    //
    // To deal with ambiguities, we might need to refine some options, such as preventing node sharing. Alternatively, we might
    // need to simply make sure that the ambiguity context we get back is at the same scope as the node being parsed
    //
    // Left recursion is currently handled via up-front transforms, but it COULD potentially be handled as a custom node type, maybe even
    // incorporating Pratt precedence parsing. This would be very in-line with the philosophy of imitating hand-made parsers
    //
    // Taking this into account, we could rethink our structure to late-bind node links even more. Starting with each start node context,
    // we can build out nodes which point to node contexts rather than nodes. The advantage of this is that it makes it easier to be more stateless
    // and thus it makes it possible to speculate (e. g. trying several strategies for parsing and picking the one that ends up being simplest).
    // 
    // Once we exhaust node contexts that still require parsing, we can go through and link everything up, possibly even doing further deduplication 
    // and other optimization that point

    /// <summary>
    /// Implements the core Forelle parser generation algorithm
    /// </summary>
    internal class ParserGenerator
    {
        private readonly Dictionary<NonTerminal, IReadOnlyList<Rule>> _rulesByProduced;
        private readonly IReadOnlyList<AmbiguityResolution> _ambiguityResolutions;
        private readonly DiscriminatorFirstFollowProviderBuilder _firstFollow;
        private readonly DiscriminatorHelper _discriminatorHelper;
        private readonly Lookup<NonTerminal, DiscriminatorContext> _discriminatorContexts = new Lookup<NonTerminal, DiscriminatorContext>();

        private readonly Queue<NodeContext> _generatorQueue = new Queue<NodeContext>();
        private readonly List<string> _errors = new List<string>();

        private readonly Dictionary<NodeContext, ParserNode> _nodesByContext = new Dictionary<NodeContext, ParserNode>();
        private readonly Dictionary<ReferenceNode, ReferenceContext> _referenceNodeContexts = new Dictionary<ReferenceNode, ReferenceContext>();

        private ParserGenerator(IReadOnlyList<Rule> rules, IEnumerable<AmbiguityResolution> ambiguityResolutions)
        {
            this._rulesByProduced = rules.GroupBy(r => r.Produced)
                .ToDictionary(g => g.Key, g => (IReadOnlyList<Rule>)g.ToList());
            this._ambiguityResolutions = ambiguityResolutions.ToArray();

            var baseFirstFollow = FirstFollowCalculator.Create(rules);
            this._firstFollow = new DiscriminatorFirstFollowProviderBuilder(baseFirstFollow);

            this._discriminatorHelper = new DiscriminatorHelper(this._rulesByProduced, this._firstFollow);
        }

        public static (Dictionary<StartSymbolInfo, ParserNode> nodes, List<string> errors) CreateParser(
            IReadOnlyList<Rule> rules,
            IEnumerable<AmbiguityResolution> ambiguityResolutions)
        {
            var generator = new ParserGenerator(rules, ambiguityResolutions);
            var nodes = generator.Generate();

            return (nodes: nodes, errors: generator._errors);
        }

        private Dictionary<StartSymbolInfo, ParserNode> Generate()
        {
            // pre-populate the generator queue with contexts for each start symbol
            var startSymbolContexts = this._rulesByProduced.Select(kvp => (startInfo: kvp.Key.SyntheticInfo as StartSymbolInfo, rules: kvp.Value))
                .Where(t => t.startInfo != null)
                .ToDictionary(t => t.startInfo, t => new NodeContext(t.rules.Select(r => r.Skip(0))));
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

            var ambiguityResolver = new Lazy<AmbiguityResolver>(
                () => new AmbiguityResolver(this._rulesByProduced, this._ambiguityResolutions, this._discriminatorContexts, this._firstFollow),
                isThreadSafe: false
            );

            var allChecks = new List<AmbiguityCheck>();
            foreach (var reference in allReferences)
            {
                var referenceContext = this._referenceNodeContexts[reference];
                if (!referenceContext.IsAmbiguous)
                {
                    reference.SetValue(this._nodesByContext[referenceContext.NodeContext]);
                }
                else
                {
                    var (checks, errors) = ambiguityResolver.Value.ResolveAmbiguity(referenceContext.NodeContext.Rules, referenceContext.NodeContext.Lookahead);
                    if (errors.Any())
                    {
                        this._errors.AddRange(errors);
                    }

                    allChecks.AddRange(checks);

                    var ambiguityResolutionNode = new AmbiguityResolutionNode(
                        checks.ToDictionary(c => c, c => this.ReferenceNodeFor(c.MappedRule))
                    );
                    reference.SetValue(ambiguityResolutionNode);
                }
            }

            foreach (var check in allChecks)
            {
                var symbolReference = this.ReferenceNodeFor(check.Context.Symbol);
                var symbolNode = symbolReference is ReferenceNode reference ? reference.Value : symbolReference;
                symbolNode.AmbiguityChecks.Add((
                    check,
                    check.Context.Leaves.Select(n => n.Symbol)
                        .OfType<NonTerminal>()
                        .Distinct()
                        .Select(s => (symbol: s, node: this.ReferenceNodeFor(s)))
                        .ToDictionary(t => t.symbol, t => t.node is ReferenceNode @ref ? @ref.Value : t.node)
                ));
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
                ?? this.CreateAmbiguityResolutionNode(lookaheadToken, rules);
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

                var suffixNode = this.ReferenceNodeFor(new NodeContext(rules.Select(r => r.Skip(prefixLength))));
                return new ParsePrefixSymbolsNode(prefix: prefix, suffixNode: suffixNode);
            }

            return null;
        }

        private static readonly IComparer<DiscriminatorHelper.DiscriminatorPrefixSearchResult> PrefixSearchResultComparer =
            Comparers.Create((DiscriminatorHelper.DiscriminatorPrefixSearchResult r) => r.IsFollowCompatible)
                // prefer prefixes which cover more total symbols
                .ThenBy(Comparers.Create((DiscriminatorHelper.DiscriminatorPrefixSearchResult r) => r.RulesToDiscriminatorRuleMapping.Values.Sum(v => v.Symbols.Count)))
                // finally, break ties by preferring root discriminators. This is useful because it keeps the follow set from growing more than
                // is needed, thus minimizing potential issues. However, this may lead to more total child discriminators being produced
                .ThenBy(Comparers.Create((DiscriminatorHelper.DiscriminatorPrefixSearchResult r) => ((DiscriminatorSymbolInfo)r.Discriminator.SyntheticInfo).ParentDiscriminator == null));

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
                this._discriminatorContexts.Add(
                    bestMatch.Discriminator,
                    new PrefixDiscriminatorContext(
                        bestMatch.RulesToDiscriminatorRuleMapping
                            .Select(kvp => new PrefixDiscriminatorContext.RuleMapping(discriminatorRule: kvp.Value, mappedRule: kvp.Key.Skip(kvp.Value.Symbols.Count))), 
                        lookaheadToken
                    )
                );

                return new MapResultNode(
                    this.ReferenceNodeFor(bestMatch.Discriminator),
                    bestMatch.RulesToDiscriminatorRuleMapping
                        .GroupBy(kvp => kvp.Value, kvp => kvp.Key.Skip(kvp.Value.Symbols.Count))
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
                    remainderMappedRules: g.Select(r => r.Skip(g.Key.Symbols.Count))
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

            this._discriminatorContexts.Add(
                newDiscriminator,
                new PrefixDiscriminatorContext(
                    newDiscriminatorRulesToRulesMapping
                        .SelectMany(
                            m => m.remainderMappedRules,
                            (m, r) => new PrefixDiscriminatorContext.RuleMapping(discriminatorRule: m.newDiscriminatorRule, mappedRule: r)
                        ),
                    lookaheadToken
                )
            );

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
            if (rules.Any(r => r.Symbols.Count > 100))
            {
                // TODO rather than simply hitting this and giving up, we should be able to handle such cases by considering the creation
                // of new prefix discriminators (as opposed to hoping that they arise "naturally"). Depending on whether we can discriminate the suffix, 
                // we may not even need these prefixes to be true discriminators as opposed to simply "recognizers" which can look past a prefix
                throw new NotSupportedException("# of symbols indicates infinite recursion. See DiscriminatorExpansionEdgeCasesTest for example cases");
            }

            // build a mapping of what we would see after passing by lookaheadToken to the rule that that suffix would indicate
            var suffixToRuleMapping = this.TryBuildSuffixToRuleMapping(lookaheadToken, rules);
            if (suffixToRuleMapping == null) { return null; }

            var ruleSymbolsAndFollowSets = suffixToRuleMapping.ToDictionary(
                kvp => kvp.Key,
                kvp => this._firstFollow.FollowOf(kvp.Value.rule.Rule),
                suffixToRuleMapping.Comparer
            );

            NonTerminal discriminator;
            IReadOnlyCollection<Rule> discriminatorRules;

            // see if we already have a discriminator which can be used here
            // first lookup to see if any discriminator has rules with the symbols we want
            var matchingDiscriminators = this._discriminatorHelper.FindDiscriminatorByRuleSymbols(ruleSymbolsAndFollowSets.Keys);
            var followCompatibleMatchingDiscriminators = matchingDiscriminators
                // do not define a discriminator in terms of itself, as this would generate a parser that simply hangs.
                // We should be safe from mutual recursion because for such cases we would have avoided creating the 
                // mutually recursive symbol set altogether due to this check
                .Where(s => s != rules[0].Produced)
                // then check that each matched rule's follow set is a superset of what we computed is correct for this case
                .Where(s => ruleSymbolsAndFollowSets.All(
                    kvp => this._firstFollow.FollowOf(this._rulesByProduced[s].Single(r => r.Symbols.SequenceEqual(kvp.Key)))
                        .IsSupersetOf(kvp.Value)
                ))
                .ToArray();
            if (followCompatibleMatchingDiscriminators.Length > 0)
            {
                discriminator = followCompatibleMatchingDiscriminators[0];
                discriminatorRules = this._rulesByProduced[discriminator].Where(r => ruleSymbolsAndFollowSets.ContainsKey(r.Symbols)).ToArray();
            }
            else
            {
                // construct a new discriminator symbol
                discriminator = NonTerminal.CreateSynthetic(
                    "T" + this._rulesByProduced.Keys.Count(k => k.SyntheticInfo is DiscriminatorSymbolInfo),
                    new DiscriminatorSymbolInfo()
                );
                var rulesAndFollowSets = suffixToRuleMapping.ToDictionary(
                    kvp => new Rule(discriminator, kvp.Key, kvp.Value.rule.Rule.ExtendedInfo),
                    kvp => this._firstFollow.FollowOf(kvp.Value.rule.Rule)
                );
                discriminatorRules = rulesAndFollowSets.Keys;

                this._rulesByProduced.Add(discriminator, rulesAndFollowSets.Keys.ToArray());
                this._firstFollow.Add(rulesAndFollowSets);
                this._generatorQueue.Enqueue(new NodeContext(rulesAndFollowSets.Keys.Select(r => r.Skip(0))));
            }

            this._discriminatorContexts.Add(
                discriminator,
                new PostTokenSuffixDiscriminatorContext(
                    discriminatorRules.Select(r => new PostTokenSuffixDiscriminatorContext.RuleMapping(
                        discriminatorRule: r, 
                        mappedRule: suffixToRuleMapping[r.Symbols].rule,
                        derivations: suffixToRuleMapping[r.Symbols].derivations
                    )),
                    lookaheadToken
                )
            );

            return new GrammarLookaheadNode(
                token: lookaheadToken,
                discriminatorParse: this.ReferenceNodeFor(discriminator),
                mapping: discriminatorRules.Select(r => (fromRule: r, toRule: suffixToRuleMapping[r.Symbols].rule))
                    .ToDictionary(t => t.fromRule, t => (t.toRule.Rule, this.ReferenceNodeFor(t.toRule)))
            );
        }

        private Dictionary<IReadOnlyList<Symbol>, (RuleRemainder rule, IReadOnlyList<PotentialParseParentNode> derivations)> TryBuildSuffixToRuleMapping(
            Token lookaheadToken, 
            IReadOnlyList<RuleRemainder> rules)
        {
            var suffixToRuleMapping = new Dictionary<IReadOnlyList<Symbol>, (RuleRemainder rule, IReadOnlyList<PotentialParseParentNode> derivations)>(EqualityComparers.GetSequenceComparer<Symbol>());
            foreach (var rule in rules)
            {
                var suffixes = this._discriminatorHelper.TryGatherPostTokenSuffixes(lookaheadToken, rule);
                if (suffixes == null)
                {
                    return null;
                }

                foreach (var suffix in suffixes)
                {
                    if (suffixToRuleMapping.ContainsKey(suffix.Key))
                    {
                        // in theory we could handle this case if the two rules with the same suffix
                        // have disjoint follow sets. However, this seems like it would come up rarely
                        return null;
                    }
                    suffixToRuleMapping.Add(suffix.Key, (rule, derivations: suffix.ToArray()));
                }
            }
            return suffixToRuleMapping;
        }

        private ParserNode CreateAmbiguityResolutionNode(Token lookaheadToken, IReadOnlyList<RuleRemainder> rules)
        {
            // to keep things simple and because a discriminator can acquire new contexts throughout generation,
            // we resolve all ambiguities at the end. Therefore when we get here we simply return a reference node
            // which is tied to the ambiguous context

            var reference = new ReferenceNode();
            this._referenceNodeContexts.Add(reference, new ReferenceContext(new NodeContext(rules, lookaheadToken), isAmbiguous: true));
            return reference;
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
            this._referenceNodeContexts.Add(reference, new ReferenceContext(nodeContext, isAmbiguous: false));
            return reference;
        }

        private ParserNode ReferenceNodeFor(NonTerminal nonTerminal)
        {
            var nodeContext = new NodeContext(this._rulesByProduced[nonTerminal].Select(r => r.Skip(0)));
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
            public ReferenceContext(NodeContext nodeContext, bool isAmbiguous)
            {
                this.NodeContext = nodeContext;
                this.IsAmbiguous = isAmbiguous;
            }

            public NodeContext NodeContext { get; }
            public bool IsAmbiguous { get; }
        }

        private string DebugGrammar => string.Join(
            Environment.NewLine + Environment.NewLine,
            this._rulesByProduced.Select(kvp => $"{kvp.Key}:{Environment.NewLine}" + string.Join(Environment.NewLine, kvp.Value.Select(r => "\t" + r)))
        );
    }
}
