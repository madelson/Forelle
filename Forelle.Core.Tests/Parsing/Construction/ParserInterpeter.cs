using Forelle.Parsing;
using Forelle.Parsing.Construction.New2;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Forelle.Tests.Parsing.Construction
{
    internal class ParserInterpeter
    {
        private IReadOnlyDictionary<ParsingContext, ParsingAction> _contextActions;
        private IReadOnlyDictionary<StartSymbolInfo, ParsingContext> _startContexts;

        private IReadOnlyList<Token> _tokens;
        private Token _endToken;

        private int _index;
        private readonly Stack<PotentialParseNode> _parsedStack = new Stack<PotentialParseNode>();

        public ParserInterpeter(
            IReadOnlyDictionary<ParsingContext, ParsingAction> contextActions,
            IReadOnlyDictionary<StartSymbolInfo, ParsingContext> startContexts)
        {
            this._contextActions = contextActions;
            this._startContexts = startContexts;
        }

        public PotentialParseNode Parse(IReadOnlyList<Token> tokens, NonTerminal symbol)
        {
            this._tokens = tokens;
            var startContext = this._startContexts.Single(kvp => kvp.Key.Symbol == symbol);
            this._endToken = startContext.Key.EndToken;

            this._index = 0;
            this._parsedStack.Clear();

            this.Parse(startContext.Value);

            var parsed = (PotentialParseParentNode)this._parsedStack.Single();
            Invariant.Require(parsed.Children.Select(ch => ch.Symbol).SequenceEqual(new Symbol[] { symbol, this._endToken }));
            return parsed.Children[0];
        }

        private PotentialParseParentNode Parse(ParsingContext context)
        {
            switch (this._contextActions[context])
            {
                case EatTokenAction eatToken:
                {
                    var nextToken = this.Peek();
                    if (nextToken != eatToken.Token) { throw new InvalidOperationException($"Expected {eatToken.Token} at index {this._index}. Found {nextToken}"); }
                    this._parsedStack.Push(new PotentialParseLeafNode(eatToken.Token));
                    ++this._index;
                    return this.Parse(eatToken.Next);
                }
                case TokenSwitchAction tokenSwitch:
                {
                    var nextToken = this.Peek();
                    return tokenSwitch.Switch.TryGetValue(nextToken, out var nextContext)
                        ? this.Parse(nextContext)
                        : throw new InvalidOperationException($"Expected one of [{string.Join(", ", tokenSwitch.Switch.Keys)}] at index {this._index}. Found {nextToken}");
                }
                case ReduceAction reduce:
                {
                    var parse = reduce.Parses.Count == 1
                        ? reduce.Parses.Single()
                        : throw new InvalidOperationException($"Multiple reductions found for context {context}");
                    this.ReduceBy(parse);
                    return parse;
                }
                case ParseContextAction parseContext:
                {
                    this.Parse(parseContext.Context);
                    return this.Parse(parseContext.Next);
                }
                default:
                    throw new InvalidOperationException("Unrecognized action!");
            }
        }

        private Token Peek() => this._index == this._tokens.Count ? this._endToken : this._tokens[this._index];

        private void ReduceBy(PotentialParseParentNode parse)
        {
            var parsedChildren = new PotentialParseNode[parse.Children.Count];
            for (var i = parse.Children.Count - 1; i >= 0; --i)
            {
                var child = parse.Children[i];
                if (child is PotentialParseParentNode subParse)
                {
                    this.ReduceBy(subParse);
                }
                
                parsedChildren[i] = this._parsedStack.Pop();
                Invariant.Require(parsedChildren[i].Symbol == child.Symbol);
                Invariant.Require(parsedChildren[i] is PotentialParseParentNode || child.Symbol is Token);
            }

            this._parsedStack.Push(new PotentialParseParentNode(parse.Rule, parsedChildren));
        }
    }
}
