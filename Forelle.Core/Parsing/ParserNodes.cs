using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Forelle.Parsing
{
    /// <summary>
    /// Represents a node in the abstract representation of a Forelle parser.
    /// 
    /// Each node describes a parsing action to take. The result of a parsing action
    /// is a <see cref="Rule"/> indicating what happened
    /// </summary>
    internal interface IParserNode
    {
    }

    internal sealed class ParseSymbolNode : IParserNode
    {
        public ParseSymbolNode(NonTerminal symbol)
        {
            this.Symbol = symbol ?? throw new ArgumentNullException(nameof(symbol));
        }

        public NonTerminal Symbol { get; }
        
        public override string ToString() => $"Parse({this.Symbol})";
    }

    internal sealed class ParseRuleNode : IParserNode
    {
        public ParseRuleNode(Rule rule) 
            : this(new RuleRemainder(rule ?? throw new ArgumentNullException(nameof(rule)), start: 0))
        {
        }

        public ParseRuleNode(RuleRemainder rule)
        {
            this.Rule = rule ?? throw new ArgumentNullException(nameof(rule));
        }

        public RuleRemainder Rule { get; }
        
        public override string ToString() => $"Parse({this.Rule})";
    }

    internal sealed class ParsePrefixSymbolsNode : IParserNode
    {
        public ParsePrefixSymbolsNode(IEnumerable<Symbol> prefixSymbols, IParserNode suffixNode)
        {
            this.PrefixSymbols = Guard.NotNullOrContainsNullAndDefensiveCopy(prefixSymbols, nameof(prefixSymbols));
            this.SuffixNode = suffixNode ?? throw new ArgumentNullException(nameof(suffixNode));
        }

        public IReadOnlyList<Symbol> PrefixSymbols { get; }
        public IParserNode SuffixNode { get; }
        
        public override string ToString() => $"Parse({string.Join(", ", this.PrefixSymbols)}), {this.SuffixNode}";
    }

    internal sealed class TokenLookaheadNode : IParserNode
    {
        public TokenLookaheadNode(IEnumerable<KeyValuePair<Token, IParserNode>> mapping)
        {
            this.Mapping = mapping.ToDictionary(
                kvp => kvp.Key, 
                kvp => kvp.Value ?? throw new ArgumentException("may not contain null", nameof(mapping))
            );
        }

        public IReadOnlyDictionary<Token, IParserNode> Mapping { get; }
        
        public override string ToString() => string.Join(" | ", this.Mapping.Select(kvp => $"{kvp.Key} => {kvp.Value}"));
    }

    internal sealed class GrammarLookaheadNode : IParserNode
    {
        public GrammarLookaheadNode(Token token, NonTerminal discriminator, IEnumerable<KeyValuePair<Rule, Rule>> mapping)
        {
            this.Token = token ?? throw new ArgumentNullException(nameof(token));
            this.Discriminator = discriminator ?? throw new ArgumentNullException(nameof(discriminator));
            if (!(discriminator.SyntheticInfo is DiscriminatorSymbolInfo)) { throw new ArgumentException("must be a discriminator symbol", nameof(discriminator)); }
            this.Mapping = mapping.ToDictionary(
                kvp => kvp.Key, 
                kvp => kvp.Value ?? throw new ArgumentException("must not contain null", nameof(mapping))
            );

            if (this.Mapping.Keys.Any(r => r.Produced != this.Discriminator))
            {
                throw new ArgumentException("key rules must produce the discriminator", nameof(mapping));
            }
            if (this.Mapping.Values.Any(r => r.Produced == this.Discriminator))
            {
                throw new ArgumentException("value rules must not produce the discriminator", nameof(mapping));
            }
        }

        public Token Token { get; }
        public NonTerminal Discriminator { get; }
        public IReadOnlyDictionary<Rule, Rule> Mapping { get; }
        
        public override string ToString() => $"{this.Token}, Parse({this.Discriminator}) {{ {string.Join(", ", this.Mapping.Select(kvp => $"{kvp.Key} => {kvp.Value}"))} }}";
    }

    internal sealed class MapResultNode : IParserNode
    {
        public MapResultNode(IParserNode mapped, IEnumerable<KeyValuePair<Rule, IParserNode>> mapping)
        {
            this.Mapped = mapped ?? throw new ArgumentNullException(nameof(mapped));
            this.Mapping = mapping.ToDictionary(kvp => kvp.Key, kvp => kvp.Value ?? throw new ArgumentException("may not contain null", nameof(mapping)));
        }

        public IParserNode Mapped { get; }
        public IReadOnlyDictionary<Rule, IParserNode> Mapping { get; }
        
        public override string ToString() => $"Map {{ {string.Join(", ", this.Mapping.Select(kvp => $"{kvp.Key} => {kvp.Value}"))} }}";
    }

    internal sealed class VariableSwitchNode : IParserNode
    {
        public VariableSwitchNode(string variableName, IParserNode trueNode, IParserNode falseNode)
        {
            this.VariableName = variableName ?? throw new ArgumentNullException(nameof(variableName));
            this.TrueNode = trueNode ?? throw new ArgumentNullException(nameof(trueNode));
            this.FalseNode = falseNode ?? throw new ArgumentNullException(nameof(falseNode));
        }

        public string VariableName { get; }
        public IParserNode TrueNode { get; }
        public IParserNode FalseNode { get; }

        public override string ToString() => $"{this.VariableName} ? {this.TrueNode} : {this.FalseNode}";
    }
}
