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

        private readonly Queue<NonTerminal> _remainingSymbols;
        private readonly Dictionary<NonTerminal, IParserNode> _nodes = new Dictionary<NonTerminal, IParserNode>();
        private readonly List<string> _errors = new List<string>();

        private readonly Dictionary<(IReadOnlyList<RuleRemainder> rules, ImmutableList<Symbol> prefix), IParserNode> _nodeCache
            = new Dictionary<(IReadOnlyList<RuleRemainder> rules, ImmutableList<Symbol> prefix), IParserNode>(
                Helpers.CreateTupleComparer<IReadOnlyList<RuleRemainder>, ImmutableList<Symbol>>(
                    EqualityComparers.GetCollectionComparer<RuleRemainder>(),
                    EqualityComparers.GetSequenceComparer<Symbol>()
                )
            );

        private ParserGenerator(IReadOnlyList<Rule> rules)
        {
            this._rulesByProduced = rules.GroupBy(r => r.Produced)
                .ToDictionary(g => g.Key, g => (IReadOnlyList<Rule>)g.ToList());

            var baseFirstFollow = FirstFollowCalculator.Create(rules);
            this._firstFollow = new DiscriminatorFirstFollowProviderBuilder(baseFirstFollow);

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
                    this.CreateParserNode(
                        this._rulesByProduced[next].Select(r => new RuleRemainder(r, start: 0)).ToArray(), 
                        prefix: ImmutableList<Symbol>.Empty
                    )
                );
            }
        }

        private IParserNode CreateParserNode(IReadOnlyList<RuleRemainder> rules, ImmutableList<Symbol> prefix)
        {
            if (this._nodeCache.TryGetValue((rules, prefix), out var existing))
            {
                return existing;
            }

            var node = this.CreateParserNodeNoCache(rules, prefix);
            this._nodeCache.Add((rules, prefix), node);
            return node;
        }

        private IParserNode CreateParserNodeNoCache(IReadOnlyList<RuleRemainder> rules, ImmutableList<Symbol> prefix)
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
                return this.CreateNonLL1ParserNode(tokenLookaheadTable.Single().Key, tokenLookaheadTable.Single().Value, prefix);
            }

            // else, create a token lookahead node mapping from the table
            return new TokenLookaheadNode(
                tokenLookaheadTable.ToDictionary(
                    kvp => kvp.Key,
                    kvp => kvp.Value.Length == 1
                        ? new ParseRuleNode(kvp.Value.Single())
                        : this.CreateNonLL1ParserNode(kvp.Key, kvp.Value, prefix)
                )
            );
        }

        private IParserNode CreateNonLL1ParserNode(Token lookaheadToken, IReadOnlyList<RuleRemainder> rules, ImmutableList<Symbol> prefix)
        {
            return this.TryCreatePrefixParserNode(lookaheadToken, rules, prefix)
                ?? this.TryCreateDiscriminatorPrefixParserNode(lookaheadToken, rules, prefix)
                ?? throw new NotImplementedException();
        }

        private IParserNode TryCreatePrefixParserNode(Token lookaheadToken, IReadOnlyList<RuleRemainder> rules, ImmutableList<Symbol> prefix)
        {
            // see if we can find a common prefix among all rules. If we can, we'll just parse the prefix
            // and then follow up by parsing the remainder
            var prefixLength = Enumerable.Range(0, count: rules.Min(r => r.Symbols.Count))
                .TakeWhile(i => rules.Skip(1).All(r => r.Symbols[i] == rules[0].Symbols[i]))
                .Select(i => i + 1)
                .LastOrDefault();

            if (prefixLength > 0)
            {
                var commonPrefix = rules[0].Symbols.Take(prefixLength);
                var suffixNode = this.CreateParserNode(
                    rules.Select(r => new RuleRemainder(r.Rule, r.Start + prefixLength)).ToArray(),
                    prefix: prefix.AddRange(commonPrefix)
                );
                return new ParsePrefixSymbolsNode(prefixSymbols: commonPrefix, suffixNode: suffixNode);
            }

            return null;
        }

        private IParserNode TryCreateDiscriminatorPrefixParserNode(Token lookaheadToken, IReadOnlyList<RuleRemainder> rules, ImmutableList<Symbol> prefix)
        {
            throw new NotImplementedException();
        }

        private IParserNode TryCreateDiscriminatorLookaheadParserNode(Token lookaheadtoken, IReadOnlyList<RuleRemainder> rules, ImmutableList<Symbol> prefix)
        {
            throw new NotImplementedException();
        }
    }
}
