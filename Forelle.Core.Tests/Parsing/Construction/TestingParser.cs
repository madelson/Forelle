using Forelle.Parsing;
using Medallion.Collections;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Forelle.Tests.Parsing.Construction
{
    internal class TestingParser
    {
        private readonly IReadOnlyDictionary<StartSymbolInfo, ParserNode> _rootNodes;

        private IReadOnlyList<Token> _tokens;
        private Token _endToken;

        private int _index, _lookaheadIndex;
        private readonly Stack<SyntaxNode> _syntaxNodes = new Stack<SyntaxNode>();

        public TestingParser(IReadOnlyDictionary<StartSymbolInfo, ParserNode> rootNodes)
        {
            this._rootNodes = rootNodes;
        }

        public SyntaxNode Parsed { get; private set; }

        public void Parse(IReadOnlyList<Token> tokens, NonTerminal symbol)
        {
            this._tokens = tokens;
            var startSymbolInfo = this._rootNodes.Keys.Single(ssi => ssi.Symbol == symbol);
            this._endToken = startSymbolInfo.EndToken;

            this._index = 0;
            this._lookaheadIndex = -1;
            this._syntaxNodes.Clear();

            this.Parse(this._rootNodes[startSymbolInfo]);

            if (this._syntaxNodes.Count != 2 && this._syntaxNodes.Peek().Symbol != this._endToken)
            {
                throw new InvalidOperationException("Bad parse!");
            }
            this._syntaxNodes.Pop();
            this.Parsed = this._syntaxNodes.Pop();
        }

        private Rule Parse(ParserNode node)
        {
            switch (node)
            {
                case ParseRuleNode ruleNode:
                    var j = 0;
                    for (var i = 0; i < ruleNode.Rule.Symbols.Count; ++i)
                    {
                        if (ruleNode.Rule.Symbols[i] is Token token)
                        {
                            this.Eat(token);
                        }
                        else
                        {
                            this.Parse(ruleNode.NonTerminalParsers[j++]);
                        }
                    }
                    this.Process(ruleNode.Rule.Rule);
                    return ruleNode.Rule.Rule;
                case TokenLookaheadNode tokenNode:
                    var nextToken = this.Peek();
                    return this.Parse(tokenNode.Mapping[nextToken]);
                case ParsePrefixSymbolsNode prefixNode:
                    foreach (var prefixElement in prefixNode.Prefix)
                    {
                        if (prefixElement.Token != null) { this.Eat(prefixElement.Token); }
                        else { this.Parse(prefixElement.Node); }
                    }
                    return this.Parse(prefixNode.SuffixNode);
                case GrammarLookaheadNode grammarLookaheadNode:
                    if (this.IsInLookahead)
                    {
                        this.Eat(grammarLookaheadNode.Token);
                        var ruleUsed = this.Parse(grammarLookaheadNode.DiscriminatorParse);
                        // TODO this potentially should perform any rule actions (e. g. state variables)
                        return grammarLookaheadNode.RuleMapping[ruleUsed];
                    }
                    else
                    {
                        this._lookaheadIndex = this._index;

                        this.Eat(grammarLookaheadNode.Token);
                        var ruleUsed = this.Parse(grammarLookaheadNode.DiscriminatorParse);

                        this._lookaheadIndex = -1;

                        return this.Parse(grammarLookaheadNode.NodeMapping[ruleUsed]);
                    }
                case MapResultNode mapResultNode:
                    if (!this.IsInLookahead) { throw new InvalidOperationException($"Encountered {mapResultNode} outside of lookahead"); }
                    var innerResult = this.Parse(mapResultNode.Mapped);
                    return this.Parse(mapResultNode.Mapping[innerResult]);
                default:
                    throw new InvalidOperationException("Unexpected node " + node.GetType());
            }
        }

        private void Process(Rule rule)
        {
            if (this.IsInLookahead) { return; }

            if (rule.ExtendedInfo.MappedRules == null)
            {
                var children = new SyntaxNode[rule.Symbols.Count];
                for (var i = children.Length - 1; i >= 0; --i)
                {
                    children[i] = this._syntaxNodes.Pop();
                }
                this._syntaxNodes.Push(new SyntaxNode(rule.Produced, children));
            }
            else
            {
                foreach (var mappedRule in rule.ExtendedInfo.MappedRules)
                {
                    this.Process(mappedRule);
                }
            }
        }

        private bool IsInLookahead
        {
            get
            {
                if (this._lookaheadIndex < 0) { return false; }
                if (this._lookaheadIndex < this._index) { throw new InvalidOperationException($"bad state: index: {this._index}, lookahead: {this._lookaheadIndex}"); }
                return true;
            }
        }

        private Token Peek()
        {
            var index = this.IsInLookahead ? this._lookaheadIndex : this._index;
            return index == this._tokens.Count ? this._endToken : this._tokens[index];
        }

        private void Eat(Token expectedToken)
        {
            var nextToken = this.Peek();
            if (nextToken != expectedToken) { throw new InvalidOperationException($"Expected {expectedToken} but found {nextToken}"); }

            if (this.IsInLookahead)
            {
                ++this._lookaheadIndex;
            }
            else
            {
                this._syntaxNodes.Push(new SyntaxNode(nextToken));
                ++this._index;
            }
        }
    }

    internal class SyntaxNode
    {
        public SyntaxNode(Token token)
        {
            this.Symbol = token;
            this.Children = Empty.Array<SyntaxNode>();
        }

        public SyntaxNode(NonTerminal symbol, IEnumerable<SyntaxNode> children)
        {
            this.Symbol = symbol;
            this.Children = children.ToArray();
        }

        public Symbol Symbol { get; }
        public IReadOnlyList<SyntaxNode> Children { get; }

        public override string ToString()
        {
            return this.Children.Count == 0
                ? this.Symbol.ToString()
                : $"{this.Symbol}({string.Join(", ", this.Children)})";
        }
    }
}
