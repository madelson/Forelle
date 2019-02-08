using Forelle.Parsing;
using Forelle.Parsing.Construction.New2;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
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

        private ImmutableHashSet<PotentialParseParentNode> Parse(ParsingContext context)
        {
            var action = this._contextActions[context];
            ImmutableHashSet<PotentialParseParentNode> nextContextResult;
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
                        nextContextResult = this.Parse(eatToken.Next);
                        break;
                    }
                case TokenSwitchAction tokenSwitch:
                    {
                        var nextToken = this.Peek();
                        nextContextResult = tokenSwitch.Switch.TryGetValue(nextToken, out var nextContext)
                            ? this.Parse(nextContext)
                            : throw new InvalidOperationException($"Expected one of [{string.Join(", ", tokenSwitch.Switch.Keys)}] at index {this._index}. Found {nextToken}");
                        break;
                    }
                case ReduceAction reduce:
                    {
                        this.ReduceBy(reduce.Parses);
                        return reduce.Parses;
                    }
                case ParseSubContextAction parseSubContext:
                    {
                        this.Parse(parseSubContext.SubContext);
                        nextContextResult = this.Parse(parseSubContext.Next);
                        break;
                    }
                case DelegateToSpecializedContextAction specializeContext:
                    {
                        if (this._scanAheadMode)
                        {
                            nextContextResult = this.Parse(specializeContext.Next);
                        }
                        else
                        {
                            this._scanAheadMode = true;
                            nextContextResult = this.Parse(specializeContext.Next);
                            this.ClearScanAhead();
                            this._scanAheadMode = false;
                        }
                        break;
                    }
                case SubContextSwitchAction subContextSwitch:
                    {
                        var subContextResult = this.Parse(subContextSwitch.SubContext);
                        nextContextResult = this.Parse(subContextResult.Select(n => subContextSwitch.Switch[n]).Only(c => c));
                        break;
                    }
                default:
                    throw new InvalidOperationException("Unrecognized action!");
            }

            // note: we could be smarter about only doing this when we know the result
            // might role up to a sub context switch
            return nextContextResult.Select(n => action.NextToCurrentNodeMapping[n])
                .Aggregate((s1, s2) => s1.Union(s2));
        }

        private Token Peek() => this._index == this._tokens.Count ? this._endToken : this._tokens[this._index];

        private void ReduceBy(ImmutableHashSet<PotentialParseParentNode> parse)
        {
            if (this._scanAheadMode)
            {
                // should we push rule in stead in the case where parse is simple?
                this._scanAheadStack.Push(new ScanAheadAction(parse));
                return;
            }

            Invariant.Require(parse.Count == 1, "Multiple reductions found!");
            var singleParse = parse.Single();
            Invariant.Require(!singleParse.Children.Any(ch => ch is PotentialParseParentNode));

            this.ReduceBy(singleParse.Rule);
        }

        private void ClearScanAhead()
        {
            // note: in an optimized implementation a single shared buffer can be used for the parser
            var buffer = new Stack<ScanAheadAction>();
            CleanScanAhead(contextSymbol: null);

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
            void CleanScanAhead(Symbol contextSymbol)
            {
                if (this._scanAheadStack.Count == 0) { return; }

                var next = this._scanAheadStack.Pop();
                if (next.Parse != null)
                {
                    if (next.Parse.Count == 1)
                    {
                        CleanScanAheadWithParse(next.Parse.Single());
                    }
                    else
                    {
                        var matches = next.Parse.Where(p => p.Symbol == contextSymbol).ToArray();
                        Invariant.Require(matches.Length == 1, "Unable to resolve multiple scanner parse");
                        CleanScanAheadWithParse(matches.Single());
                    }
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
                    if (parse.Children[i] is PotentialParseParentNode parent
                        // when we encounter a node like A(`Placeholder<A(B)>`), just process it like we found an A since the substructure
                        // represented by the placeholder will exist beneath us in the parse stack anyway. TODO in the future if we allow
                        // per-rule follow sets or other ways to contextually restrict rules then we would need to change this to pass through 
                        // the placeholder symbol and resolve the underlying substructure based on it (A(B) is more specific than resolving 
                        // just A)
                        && !(parent.Rule.Symbols.Count == 1 && parent.Rule.Symbols[0].SyntheticInfo is SubContextPlaceholderSymbolInfo))
                    {
                        CleanScanAheadWithParse(parent);
                    }
                    else
                    {
                        CleanScanAhead(parse.Children[i].Symbol);
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
            public ScanAheadAction(ImmutableHashSet<PotentialParseParentNode> parse) { this._value = parse; }

            public ParseNode ParsedToken => this._value as ParseNode;
            public Rule Rule => this._value as Rule;
            public ImmutableHashSet<PotentialParseParentNode> Parse => this._value as ImmutableHashSet<PotentialParseParentNode>;

            public override string ToString() => this.Parse != null ? string.Join(" | ", this.Parse) : this._value.ToString();
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
