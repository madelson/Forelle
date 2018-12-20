using Medallion.Collections;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;

namespace Forelle.Parsing
{
    /// <summary>
    /// Represents a node in the abstract representation of a Forelle parser.
    /// 
    /// Each node describes a parsing action to take. The result of a parsing action
    /// is a <see cref="Rule"/> indicating what happened
    /// </summary>
    internal abstract class ParserNode
    {
        public virtual IReadOnlyList<ParserNode> ChildNodes => Empty.ReadOnlyList<ParserNode>();

        // todo not a great design...
        public List<(AmbiguityCheck Check, IReadOnlyDictionary<NonTerminal, ParserNode> NonTerminalParsers)> AmbiguityChecks = new List<(AmbiguityCheck Check, IReadOnlyDictionary<NonTerminal, ParserNode> NonTerminalParsers)>();
    }

    internal class AmbiguityResolutionNode : ParserNode
    {
        public AmbiguityResolutionNode(IReadOnlyDictionary<AmbiguityCheck, ParserNode> checksToNodes)
        {
            this.ChecksToNodes = checksToNodes.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
        }

        public IReadOnlyDictionary<AmbiguityCheck, ParserNode> ChecksToNodes { get; }
    }

    internal class AmbiguityCheck
    {
        public AmbiguityCheck(
            PotentialParseParentNode context,
            RuleRemainder mappedRule,
            int priority)
        {
            this.Context = context ?? throw new ArgumentNullException(nameof(context));
            this.MappedRule = mappedRule ?? throw new ArgumentNullException(nameof(mappedRule));
            this.Priority = priority;
            
            if (!this.Context.Leaves.Any(l => l.Symbol is Token && l.CursorPosition == 0))
            {
                throw new ArgumentException("Must have a leaf marked with the cursor", nameof(context));
            }
        }
        
        public PotentialParseParentNode Context { get; }
        public RuleRemainder MappedRule { get; }
        public int Priority { get; }
        
        public override string ToString() => $"{this.Context}: PRIORITY {this.Priority}";
    }

    internal sealed class ParseRuleNode : ParserNode
    {
        public ParseRuleNode(
            RuleRemainder rule,
            IEnumerable<ParserNode> nonTerminalParsers)
        {
            this.Rule = rule ?? throw new ArgumentNullException(nameof(rule));
            this.NonTerminalParsers = new NodeList(nonTerminalParsers);
            
            if (this.NonTerminalParsers.Count != this.Rule.Symbols.Count(s => s is NonTerminal))
            {
                throw new ArgumentOutOfRangeException(nameof(nonTerminalParsers), this.NonTerminalParsers.Count, $"The number of non-terminal parsers must match the number of non-terminal symbols in the supplied {nameof(rule)} {rule}");
            }
        }

        public RuleRemainder Rule { get; }
        public IReadOnlyList<ParserNode> NonTerminalParsers { get; }
        public override IReadOnlyList<ParserNode> ChildNodes => this.NonTerminalParsers;
        
        public override string ToString() => $"Parse({this.Rule})";
    }

    /// <summary>
    /// A <see cref="ParserNode"/> which acts only as a lazy, set-once pointer to another <see cref="ParserNode"/>.
    /// This type allows <see cref="ParserNode"/> trees to be immutable and yet have circular references.
    /// 
    /// This construct also allows the node generation algorithm to be rigorous in avoiding duplicates. Each step
    /// of the algorithm produces a single new <see cref="ParserNode"/>, with any non-computed internal nodes filled
    /// in by <see cref="ReferenceNode"/>s. The references are then set once the algorithm has finished computing all
    /// <see cref="ParserNode"/>s
    /// </summary>
    internal sealed class ReferenceNode : ParserNode
    {
        private IReadOnlyList<ParserNode> _childNodes;

        public override IReadOnlyList<ParserNode> ChildNodes => Volatile.Read(ref this._childNodes) ?? throw new InvalidOperationException(nameof(SetValue) + " has not yet been called");
        public ParserNode Value => this.TryGetValue(out var value) ? value : throw new InvalidOperationException(nameof(SetValue) + " has not yet been called");
        public bool IsValueCreated => this.TryGetValue(out var ignored);

        public bool TryGetValue(out ParserNode value)
        {
            var childNodes = Volatile.Read(ref this._childNodes);
            if (childNodes != null)
            {
                value = DeepGetValue(childNodes[0]);
                return true;
            }

            value = null;
            return false;
        }

        public void SetValue(ParserNode node)
        {
            if (node == null) { throw new ArgumentNullException(nameof(node)); }

            var nodeValue = DeepGetValue(node);
            if (nodeValue == this) { throw new ArgumentException("must not create a circular reference chain", nameof(node)); }

            // note: as part of the circularity check, we throw away nodes earlier in the chain for efficiency
            if (Interlocked.CompareExchange(ref this._childNodes, new[] { nodeValue }, comparand: null) != null)
            {
                throw new InvalidOperationException("value was already set");
            }
        }

        private static ParserNode DeepGetValue(ParserNode node) 
            => node is ReferenceNode reference && reference.TryGetValue(out var value) ? DeepGetValue(value) : node;

        public override string ToString() => $"Reference({(this.TryGetValue(out var value) ? value.ToString() : "?")})";
    }

    internal sealed class ParsePrefixSymbolsNode : ParserNode
    {
        public ParsePrefixSymbolsNode(IEnumerable<TokenOrParserNode> prefix, ParserNode suffixNode)
        {
            this.Prefix = Guard.NotNullOrContainsNullAndDefensiveCopy(prefix, nameof(prefix));
            this.ChildNodes = new NodeList(
                this.Prefix.Select(topn => topn.Node).Where(n => n != null).Concat(new[] { suffixNode })
            );
        }

        public IReadOnlyList<TokenOrParserNode> Prefix { get; }
        public ParserNode SuffixNode => this.ChildNodes[this.ChildNodes.Count - 1];
        public override IReadOnlyList<ParserNode> ChildNodes { get; }
        
        public override string ToString() => $"Parse({string.Join(", ", this.Prefix)}), {this.SuffixNode}";
    }

    internal sealed class TokenLookaheadNode : ParserNode
    {
        public TokenLookaheadNode(IEnumerable<KeyValuePair<Token, ParserNode>> mapping)
        {
            this.Mapping = new NodeValueDictionary<Token>(mapping);
            this.ChildNodes = new NodeList(this.Mapping.Values);
        }

        public IReadOnlyDictionary<Token, ParserNode> Mapping { get; }
        public override IReadOnlyList<ParserNode> ChildNodes { get; }
        
        public override string ToString() => string.Join(" | ", this.Mapping.Select(kvp => $"{kvp.Key} => {kvp.Value}"));
    }

    internal sealed class GrammarLookaheadNode : ParserNode
    {
        public GrammarLookaheadNode(Token token, ParserNode discriminatorParse, IEnumerable<KeyValuePair<Rule, (Rule rule, ParserNode node)>> mapping)
        {
            this.Token = token ?? throw new ArgumentNullException(nameof(token));
            var mappingArray = mapping.ToArray();
            this.RuleMapping = mappingArray.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.rule);
            this.NodeMapping = new NodeValueDictionary<Rule>(mappingArray.Select(kvp => new KeyValuePair<Rule, ParserNode>(kvp.Key, kvp.Value.node)));
            this.ChildNodes = new NodeList(new[] { discriminatorParse }.Concat(this.NodeMapping.Values));
        }

        public Token Token { get; }
        public ParserNode DiscriminatorParse => this.ChildNodes[0];
        public IReadOnlyDictionary<Rule, Rule> RuleMapping { get; }
        public IReadOnlyDictionary<Rule, ParserNode> NodeMapping { get; }
        public override IReadOnlyList<ParserNode> ChildNodes { get; }

        public override string ToString() => $"{this.Token}, Parse({this.DiscriminatorParse}) {{ {string.Join(", ", this.RuleMapping.Select(kvp => $"{kvp.Key} => {kvp.Value}"))} }}";
    }

    // todo could merge with grammar lookahead?
    internal sealed class MapResultNode : ParserNode
    {
        public MapResultNode(ParserNode mapped, IEnumerable<KeyValuePair<Rule, ParserNode>> mapping)
        {
            this.Mapping = new NodeValueDictionary<Rule>(mapping);
            this.ChildNodes = new NodeList(new[] { mapped }.Concat(this.Mapping.Values));
        }

        public ParserNode Mapped => this.ChildNodes[0];
        public IReadOnlyDictionary<Rule, ParserNode> Mapping { get; }
        public override IReadOnlyList<ParserNode> ChildNodes { get; }
        
        public override string ToString() => $"Map {{ {string.Join(", ", this.Mapping.Select(kvp => $"{kvp.Key} => {kvp.Value}"))} }}";
    }

    internal sealed class VariableSwitchNode : ParserNode
    {
        public VariableSwitchNode(string variableName, ParserNode trueNode, ParserNode falseNode)
        {
            this.VariableName = variableName ?? throw new ArgumentNullException(nameof(variableName));
            this.ChildNodes = new NodeList(new[] { trueNode, falseNode });
        }

        public string VariableName { get; }
        public ParserNode TrueNode => this.ChildNodes[0];
        public ParserNode FalseNode => this.ChildNodes[1];
        public override IReadOnlyList<ParserNode> ChildNodes { get; }
        
        public override string ToString() => $"{this.VariableName} ? {this.TrueNode} : {this.FalseNode}";
    }
}
