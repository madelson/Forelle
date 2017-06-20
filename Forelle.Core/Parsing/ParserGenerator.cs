using Medallion.Collections;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;

namespace Forelle.Parsing
{
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

            throw new NotImplementedException();
        }
    }
}
