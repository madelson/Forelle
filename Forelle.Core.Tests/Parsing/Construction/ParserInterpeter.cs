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

        // scan-ahead mode is used when the parser encounters a node that will reduce to
        // a complex parse. In scan-ahead mode, shifts and reduces are buffered in the scan-ahead
        // stack rather than being applied directly. When the complex parse finishes, this allows
        // us to replay what happened and process it correctly in the context of what we now know
        // the complex parse to be
        private bool _scanAheadMode;
        private readonly Stack<ScanAheadAction> _scanAheadStack = new Stack<ScanAheadAction>();
        
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
            this._scanAheadMode = false;
            this._scanAheadStack.Clear();
            
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
                        var tokenNode = new ParseNode(eatToken.Token, this._index);
                        if (this._scanAheadMode) { this._scanAheadStack.Push(new ScanAheadAction(tokenNode)); }
                        else { this._parsedStack.Push(tokenNode); }
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
                case DelegateToSpecializedContextAction specializeContext:
                    {
                        if (this._scanAheadMode)
                        {
                            return this.Parse(specializeContext.Next);
                        }

                        this._scanAheadMode = true;
                        var result = this.Parse(specializeContext.Next);
                        this.ClearScanAhead();
                        this._scanAheadMode = false;
                        return result;
                    }
                default:
                    throw new InvalidOperationException("Unrecognized action!");
            }
        }

        private Token Peek() => this._index == this._tokens.Count ? this._endToken : this._tokens[this._index];

        private void ReduceBy(PotentialParseParentNode parse)
        {
            if (this._scanAheadMode)
            {
                // should we push rule in stead in the case where parse is simple?
                this._scanAheadStack.Push(new ScanAheadAction(parse));
                return;
            }

            Invariant.Require(!parse.Children.Any(ch => ch is PotentialParseParentNode));

            this.ReduceBy(parse.Rule);
        }

        private void ClearScanAhead()
        {
            // note: in an optimized implementation a single shared buffer can be used for the parser
            var buffer = new Stack<ScanAheadAction>();
            CleanScanAhead();

            while (buffer.Count > 0) // replay from the buffer
            {
                var next = buffer.Pop();
                if (next.ParsedToken != null)
                {
                    this._parsedStack.Push(next.ParsedToken);
                }
                else
                {
                    this.ReduceBy(next.Rule);
                }
            }

            // "cleans" the scan ahead stack by removing complex parse nodes
            void CleanScanAhead()
            {
                if (this._scanAheadStack.Count == 0) { return; }

                var next = this._scanAheadStack.Pop();
                if (next.Parse != null)
                {
                    CleanScanAheadWithParse(next.Parse);
                }
                else
                {
                    buffer.Push(next);
                }
            }

            // for a complex parse, we need to thread it's rule parses 
            // appropriately among the other actions
            void CleanScanAheadWithParse(PotentialParseParentNode parse)
            {
                buffer.Push(new ScanAheadAction(parse.Rule));

                for (var i = parse.Children.Count - 1; i >= 0; --i)
                {
                    if (parse.Children[i] is PotentialParseParentNode parent)
                    {
                        CleanScanAheadWithParse(parent);
                    }
                    else
                    {
                        CleanScanAhead();
                    }
                }
            }
        }

        private void ReduceBy(Rule rule)
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
                this._parsedStack.Push(new ParseNode(rule.Produced, children, GetStartIndex()));

                int GetStartIndex()
                {
                    if (this._parsedStack.Count == 0) { return 0; }
                    var previous = this._parsedStack.Peek();
                    return previous.StartIndex + previous.Width;
                }
            }
        }

        private struct ScanAheadAction
        {
            private readonly object _value;

            public ScanAheadAction(ParseNode token) { this._value = token; }
            public ScanAheadAction(Rule rule) { this._value = rule; }
            public ScanAheadAction(PotentialParseParentNode parse) { this._value = parse; }

            public ParseNode ParsedToken => this._value as ParseNode;
            public Rule Rule => this._value as Rule;
            public PotentialParseParentNode Parse => this._value as PotentialParseParentNode;

            public override string ToString() => this._value.ToString();
        }
    }

    internal class ParseNode
    {
        public ParseNode(Token token, int startIndex)
        {
            this.Symbol = token;
            this.Children = Array.Empty<ParseNode>();
            this.StartIndex = startIndex;
            this.Width = 1;
        }
        
        public ParseNode(NonTerminal symbol, IEnumerable<ParseNode> children, int startIndex)
        {
            this.Symbol = symbol;
            this.Children = children.ToArray();
            Invariant.Require(this.Children.Count == 0 || this.Children[0].StartIndex == startIndex);
            this.StartIndex = startIndex;
            this.Width = this.Children.Sum(ch => ch.Width);
        }

        public Symbol Symbol { get; }
        public IReadOnlyList<ParseNode> Children { get; }
        public int StartIndex { get; }
        public int Width { get; }

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
