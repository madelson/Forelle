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

        private readonly Queue<NonTerminal> _remainingSymbols;
        private readonly Dictionary<NonTerminal, IParserNode> _nodes = new Dictionary<NonTerminal, IParserNode>();
        private readonly List<string> _errors = new List<string>();

        private readonly Dictionary<IReadOnlyList<RuleRemainder>, IParserNode> _nodeCache
            = new Dictionary<IReadOnlyList<RuleRemainder>, IParserNode>(
                EqualityComparers.GetCollectionComparer<RuleRemainder>()
            );

        private ParserGenerator(IReadOnlyList<Rule> rules)
        {
            this._rulesByProduced = rules.GroupBy(r => r.Produced)
                .ToDictionary(g => g.Key, g => (IReadOnlyList<Rule>)g.ToList());

            var baseFirstFollow = FirstFollowCalculator.Create(rules);
            this._firstFollow = new DiscriminatorFirstFollowProviderBuilder(baseFirstFollow);

            this._discriminatorHelper = new DiscriminatorHelper(this._rulesByProduced, this._firstFollow);

            this._remainingSymbols = new Queue<NonTerminal>(this._rulesByProduced.Keys);
        }

        public static (Dictionary<NonTerminal, IParserNode> nodes, List<string> errors) CreateParser(IReadOnlyList<Rule> rules)
        {
            var generator = new ParserGenerator(rules);
            generator.Generate();

            return (nodes: generator._nodes, errors: generator._errors);
        }

        private void Generate()
        {
            while (this._remainingSymbols.Count > 0)
            {
                var next = this._remainingSymbols.Dequeue();
                this._nodes.Add(
                    next, 
                    this.CreateParserNode(this._rulesByProduced[next].Select(r => new RuleRemainder(r, start: 0)).ToArray())
                );
            }
        }

        private IParserNode CreateParserNode(IReadOnlyList<RuleRemainder> rules)
        {
            if (this._nodeCache.TryGetValue(rules, out var existing))
            {
                return existing;
            }

            var node = this.CreateParserNodeNoCache(rules);
            this._nodeCache.Add(rules, node);
            return node;
        }

        private IParserNode CreateParserNodeNoCache(IReadOnlyList<RuleRemainder> rules)
        {
            // if we only have one rule, we just parse that
            if (rules.Count == 1)
            {
                return new ParseRuleNode(rules.Single());
            }

            // next, see what we can do with LL(1) single-token lookahead
            var tokenLookaheadTable = rules.SelectMany(r => this._firstFollow.NextOf(r), (r, t) => (rule: r, token: t))
                .GroupBy(t => t.token, t => t.rule)
                .ToDictionary(g => g.Key, g => g.ToArray());

            // if there is only one entry in the table, just create a non-LL(1) node for that entry
            // we know that this must be non-LL(1) because we already checked for the single-rule case above
            if (tokenLookaheadTable.Count == 1)
            {
                return this.CreateNonLL1ParserNode(tokenLookaheadTable.Single().Key, tokenLookaheadTable.Single().Value);
            }

            // else, create a token lookahead node mapping from the table
            return new TokenLookaheadNode(
                tokenLookaheadTable.ToDictionary(
                    kvp => kvp.Key,
                    kvp => kvp.Value.Length == 1
                        ? new ParseRuleNode(kvp.Value.Single())
                        : this.CreateNonLL1ParserNode(kvp.Key, kvp.Value)
                )
            );
        }

        private IParserNode CreateNonLL1ParserNode(Token lookaheadToken, IReadOnlyList<RuleRemainder> rules)
        {
            return this.TryCreatePrefixParserNode(lookaheadToken, rules)
                ?? this.TryCreateDiscriminatorPrefixParserNode(lookaheadToken, rules)
                ?? this.TryCreateDiscriminatorLookaheadParserNode(lookaheadToken, rules)
                ?? throw new NotImplementedException();
        }

        private IParserNode TryCreatePrefixParserNode(Token lookaheadToken, IReadOnlyList<RuleRemainder> rules)
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
                var commonPrefix = rules[0].Symbols.Take(prefixLength);
                var suffixNode = this.CreateParserNode(rules.Select(r => new RuleRemainder(r.Rule, r.Start + prefixLength)).ToArray());
                return new ParsePrefixSymbolsNode(prefixSymbols: commonPrefix, suffixNode: suffixNode);
            }

            return null;
        }

        private IParserNode TryCreateDiscriminatorPrefixParserNode(Token lookaheadToken, IReadOnlyList<RuleRemainder> rules)
        {
            // if we are producing a discriminator, see if an existing discriminator is a prefix. This
            // lets us handle recursion within the lookahead grammar

            var produced = rules.Only(r => r.Produced);
            if (!(produced.SyntheticInfo is DiscriminatorSymbolInfo)) { return null; }

            // TODO there is a lot more to do in this block (exact match handling, sub discriminators)
            // TODO for better deduplication we should consider always leaving stub nodes rather than making recursive calls.
            // those nodes get replaced in a final pass at the end

            var match = this._rulesByProduced
                // TODO used to just check for equality, but this would find T0 as a match for T0'. Now we check for
                // that as well. This is redundant with the final check but might help for speed TODO && true
                .Where(kvp => kvp.Key != produced && kvp.Key.SyntheticInfo is DiscriminatorSymbolInfo dsi && true)
                //.Where(kvp => kvp.Key != produced && kvp.Value.All(i => i.Symbol != produced))
                .Select(kvp => new { discriminator = kvp.Key, mapping = this.TryMapRulesToDiscriminatorPrefixRules(kvp.Key, rules, lookaheadToken) })
                .Where(t => t.mapping != null)

                // TODO can we handle exact match differently?
                // don't consider a prefix mapping if it accounts for all symbols in the rules, since this can send us around
                // in a loop when two discriminators have the same rules
                //.Where(t => t.mapping.Any(kvp => kvp.Key.Symbols.Count > kvp.Value.Symbols.Count))
                .FirstOrDefault();
            if (match == null) { return null; }

            // compute the effective follow of each mapped prefix rule based on the remaining suffix
            var followMapping = match.mapping.GroupBy(kvp => kvp.Value, kvp => kvp.Key)
                .ToDictionary(
                    g => g.Key,
                    g => g.Select(rr => new RuleRemainder(rr.Rule, start: rr.Start + g.Key.Symbols.Count))
                        .Select(rr => this._firstFollow.NextOf(rr))
                        .Aggregate((s1, s2) => s1.Union(s2))
                );

            // if the mapped discriminator rules fully encompass the follow sets of the mapped prefixes,
            // then we can directly use that discriminator
            if (followMapping.All(kvp => kvp.Value.Except(this._firstFollow.FollowOf(kvp.Key)).Count == 0))
            {
                return new MapResultNode(
                    new ParseSymbolNode(match.discriminator),
                    match.mapping.GroupBy(kvp => kvp.Value, kvp => new RuleRemainder(kvp.Key.Rule, start: kvp.Key.Start + kvp.Value.Symbols.Count))
                        .ToDictionary(
                            g => g.Key,
                            g => this.CreateParserNode(g.ToArray())
                        )
                );
            }

            throw new NotImplementedException();
        }

        /// <summary>
        /// Attempts to find a mapping between the rules for <paramref name="discriminator"/> and <paramref name="toMap"/>
        /// that satisfies the following criteria:
        /// 
        /// * PREFIX: for all mappings, the symbols in the discriminator rule form a prefix of the symbols in the mapped rule
        /// * LONGEST-PREFIX: for all mappings, there is no discriminator rule with more symbols that would also form a prefix of the mapped rule symbols
        /// * LOOKAHEAD: for all mappings, the mapped discriminator rule has <paramref name="lookaheadToken"/> in its next set.
        ///     This is important because we need to be able to parse the mapped discriminator starting with <paramref name="lookaheadToken"/>
        /// * ALL-RULES: all rules in <paramref name="toMap"/> are mapped
        /// * MANY-TO-MANY: it is NOT the case that all rules in <paramref name="toMap"/> are mapped to the same discriminator symbol. This could
        ///     happen with any empty rule. We exclude it because it doesn't help us narrow things down at all
        /// * ALL-DISCRIMINATOR-RULES: all rules for <paramref name="discriminator"/> are mapped UNLESS they cannot appear in the context of <paramref name="lookaheadToken"/>. 
        ///     This restriction is somewhat questionable, since if we did find such a result it would simply mean that there will be a parsing error
        /// </summary>
        private Dictionary<RuleRemainder, Rule> TryMapRulesToDiscriminatorPrefixRules(NonTerminal discriminator, IReadOnlyCollection<RuleRemainder> toMap, Token lookaheadToken)
        {
            // LOOKAHEAD constraint
            var validDiscriminatorRules = this._rulesByProduced[discriminator].Where(r => this._firstFollow.NextOf(r).Contains(lookaheadToken))
                .ToList();

            var result = new Dictionary<RuleRemainder, Rule>();
            foreach (var rule in validDiscriminatorRules)
            {
                // PREFIX constraint
                foreach (var match in toMap.Where(r => r.Symbols.Take(rule.Symbols.Count).SequenceEqual(rule.Symbols)))
                {
                    if (!result.TryGetValue(match, out var existingMapping)
                        // LONGEST-PREFIX constraint
                        || existingMapping.Symbols.Count < rule.Symbols.Count)
                    {
                        result[match] = rule;
                    }
                }
            }

            // ALL-RULES constraint
            if (result.Count != toMap.Count) { return null; }

            // MANY-TO-MANY constraint
            if (result.Values.Distinct().Take(2).Count() < 2) { return null; }

            // ALL-DISCRIMINATOR-RULES constraint 
            if (validDiscriminatorRules.Except(result.Values).Any())
            {
                return null;
            }

            return result;
        }

        private IParserNode TryCreateDiscriminatorLookaheadParserNode(Token lookaheadToken, IReadOnlyList<RuleRemainder> rules)
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
            this._remainingSymbols.Enqueue(discriminator);

            return new GrammarLookaheadNode(
                token: lookaheadToken,
                discriminator: discriminator,
                mapping: rulesAndFollowSets.Keys.ToDictionary(r => r, r => suffixToRuleMapping[r.Symbols].Rule)
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

        private string DebugGrammar => string.Join(
            Environment.NewLine + Environment.NewLine,
            this._rulesByProduced.Select(kvp => $"{kvp.Key}:{Environment.NewLine}" + string.Join(Environment.NewLine, kvp.Value.Select(r => "\t" + r)))
        );
    }
}
