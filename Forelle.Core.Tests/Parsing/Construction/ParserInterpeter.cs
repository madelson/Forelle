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
        private readonly Stack<ParseNode> _parsedStack = new Stack<ParseNode>();
        
        public ParserInterpeter(
            IReadOnlyDictionary<ParsingContext, ParsingAction> contextActions,
            IReadOnlyDictionary<StartSymbolInfo, ParsingContext> startContexts)
        {
            this._contextActions = contextActions;
            this._startContexts = startContexts;
        }

        public ParseNode Parse(IReadOnlyList<Token> tokens, NonTerminal symbol)
        {
            this._tokens = tokens;
            var startContext = this._startContexts.Single(kvp => kvp.Key.Symbol == symbol);
            this._endToken = startContext.Key.EndToken;

            this._index = 0;
            this._parsedStack.Clear();

            this.Parse(startContext.Value);

            Invariant.Require(this._parsedStack.Select(n => n.Symbol).SequenceEqual(new Symbol[] { this._endToken, symbol }));
            return this._parsedStack.Last();
        }

        private PotentialParseParentNode Parse(ParsingContext context)
        {
            switch (this._contextActions[context])
            {
                case EatTokenAction eatToken:
                {
                    var nextToken = this.Peek();
                    if (nextToken != eatToken.Token) { throw new InvalidOperationException($"Expected {eatToken.Token} at index {this._index}. Found {nextToken}"); }
                    this._parsedStack.Push(new ParseNode(eatToken.Token));
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
            // to make this work, I think we want to store some count or index indicator with nodes in the
            // stack which lets us successfully pop off and buffer later leaves so that we can perform intermediate
            // reductions
            if (parse.Children.Any(ch => ch is PotentialParseParentNode)) { throw new NotSupportedException("deep parses"); }

            ReduceBy(parse.Rule);

            void ReduceBy(Rule rule)
            {
                if (rule.ExtendedInfo.MappedRules != null)
                {
                    foreach (var mappedRule in rule.ExtendedInfo.MappedRules)
                    {
                        ReduceBy(mappedRule);
                    }
                }
                else
                {
                    var children = new ParseNode[rule.Symbols.Count];
                    for (var i = rule.Symbols.Count - 1; i >= 0; --i)
                    {
                        children[i] = this._parsedStack.Pop();
                    }
                    this._parsedStack.Push(new ParseNode(rule.Produced, children));
                }
            }
        }
    }

    internal class ParseNode
    {
        public ParseNode(Token token)
        {
            this.Symbol = token;
            this.Children = Array.Empty<ParseNode>();
        }

        public ParseNode(NonTerminal symbol, IEnumerable<ParseNode> children)
        {
            this.Symbol = symbol;
            this.Children = children.ToArray();
        }

        public Symbol Symbol { get; }
        public IReadOnlyList<ParseNode> Children { get; }

        public override string ToString()
        {
            return this.Symbol is Token ? ToString(this.Symbol) : $"{ToString(this.Symbol)}({string.Join(" ", this.Children)})";

            string ToString(Symbol symbol) => symbol.Name.Any(char.IsWhiteSpace)
                || symbol.Name.IndexOf('(') >= 0
                || symbol.Name.IndexOf(')') >= 0
                ? $"'{symbol.Name}'"
                : symbol.Name;
        }
    }
}
