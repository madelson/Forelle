using Forelle.Parsing;
using Forelle.Parsing.Preprocessing;
using Forelle.Tests.Parsing.Construction;
using Medallion.Collections;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Forelle.Tests.Parsing
{
    /// <summary>
    /// Implements a GLL-like algorithm that uses a PEG-like mechanism to resolve conflicts
    /// </summary>
    internal class TestingGraphPegParserInterpreter
    {
        private readonly ILookup<NonTerminal, Rule> _rulesByProduced;
        private readonly IReadOnlyDictionary<Rule, int> _ruleIndices;
        private readonly IReadOnlyDictionary<Rule, ImmutableHashSet<Token>> _nextSetsByRule;

        /// <summary>
        /// <see cref="_heads"/> tracks the current stack heads
        /// <see cref="_newHeads"/> buffers the stack heads being created
        /// <see cref="_partiallyExpandedNewHeads"/> tracks intermediate expansions at the current position. This makes them easy to find for future merging
        /// </summary>
        private List<GraphStructureStackNode> _heads = new List<GraphStructureStackNode>(),
            _newHeads = new List<GraphStructureStackNode>(),
            _partiallyExpandedNewHeads = new List<GraphStructureStackNode>();
        private IReadOnlyList<Token> _tokens;
        private Token _endToken;
        private int _index;
        private InternalParseNode _result;

        public TestingGraphPegParserInterpreter(IReadOnlyList<Rule> rules)
        {
            var withStartSymbols = StartSymbolAdder.AddStartSymbols(rules);

            var firstFollow = FirstFollowCalculator.Create(withStartSymbols);

            this._rulesByProduced = withStartSymbols.ToLookup(r => r.Produced);
            this._ruleIndices = rules.Select((r, i) => (r, i)).ToDictionary(t => t.r, t => t.i);
            this._nextSetsByRule = withStartSymbols.ToDictionary(
                r => r,
                r => firstFollow.NextOf(r)
            );
        }

        public Construction.ParseNode Parse(IReadOnlyList<Token> tokens, NonTerminal symbol)
        {
            this.ResetParser(tokens, symbol);

            while (this._index <= this._tokens.Count)
            {
                Console.WriteLine($"{this._heads.Count} heads @{this._index}, {this._heads.Sum(h => h.PreviousNodes.Count)} paths");
                this.AdvanceParser();
            }

            Invariant.Require(this._result != null);
            return this._result.Children.Head.ToParseNode();
        }

        private void ResetParser(IReadOnlyList<Token> tokens, NonTerminal symbol)
        {
            this._result = null;
            this._tokens = tokens;
            this._index = 0;

            var (startInfo, startSymbol) = this._rulesByProduced.Keys()
                .Select(s => (startInfo: s.SyntheticInfo as StartSymbolInfo, startSymbol: s))
                .Single(si => si.startInfo?.Symbol == symbol);
            this._endToken = startInfo.EndToken;

            // "prime" the parser
            this._partiallyExpandedNewHeads.Clear();
            this._newHeads.Clear();
            foreach (var startRule in this._rulesByProduced[startSymbol])
            {
                this.Push(new GraphStructureStackNode(startRule.Skip(0), position: 0));
            }
            this.SwapHeadsAndNewHeads();
        }

        private void AdvanceParser()
        {
            if (this._heads.Count == 0)
            {
                // for real error handling, we can detect this after computing _newHeads. If there are no new heads,
                // then we can either skip the token or insert a missing token based on the heads we have

                throw new InvalidOperationException($"Unexpected token {this.Peek()} @{this._index}");
            }

            this._partiallyExpandedNewHeads.Clear();
            this._newHeads.Clear();

            // consume the input
            var currentToken = this.Peek();
            var parsedToken = new InternalParseNode(currentToken, this._index);
            ++this._index;

            // for each head, advance it and push the advanced node
            foreach (var head in this._heads)
            {
                Invariant.Require(head.Rule.Symbols[0] == currentToken);

                var toPush = new GraphStructureStackNode(head.Rule.Skip(1), position: this._index);
                toPush.AddPrevious(head, parsedToken);
                this.Push(toPush);
            }
            this.SwapHeadsAndNewHeads();
        }

        private void Push(GraphStructureStackNode node)
        {
            if (node.Rule.Symbols.Count == 0)
            {
                // we've reached the end of a rule => reduce!
                this.Reduce(node, ImmutableLinkedList<InternalParseNode>.Empty);
            }
            else if (node.Rule.Symbols[0] is NonTerminal nonTerminal)
            {
                // if the next symbol to be parsed is a non-terminal, we push the current
                // node onto the partial expansion list and expand that non-terminal based
                // on the lookahead symbol

                if (AddOrMerge(node, this._partiallyExpandedNewHeads))
                {
                    foreach (var expansionRule in this._rulesByProduced[nonTerminal]
                        .Where(r => this._nextSetsByRule[r].Contains(this.Peek())))
                    {
                        var toPush = new GraphStructureStackNode(expansionRule.Skip(0), this._index);
                        toPush.AddPrevious(node, parse: null);
                        this.Push(toPush);
                    }
                }
            }
            else if (node.Rule.Symbols[0] == this.Peek())
            {
                AddOrMerge(node, this._newHeads);
            }
        }

        private void Reduce(GraphStructureStackNode node, ImmutableLinkedList<InternalParseNode> children)
        {
            if (node.Rule.Start > 0)
            {
                // this state means that we're walking backwards within the same rule, e. g. from
                // E -> ( E .) to E -> ( .E ) to do this we simply follow each path, prepending the
                // parse node from that path

                foreach (var (previousNode, previousChild) in node.PreviousNodes)
                {
                    this.Reduce(previousNode, children.Prepend(previousChild));
                }
            }
            else
            {
                // this state means we reached the beginning of a rule and we must reduce back up to
                // the parent rule (e. g. we're at E -> .( E ) and we are going back to S -> .E ;)

                var parseNode = new InternalParseNode(node.Rule.Rule, children, startIndex: this._index - children.Sum(n => n.Width));
                if (node.PreviousNodes.Count == 0)
                {
                    // if there are no outgoing paths, then we've reached the end of the parse

                    // even for an ambiguous grammar, we can't ever have top-level ambiguity because any 
                    // ambiguity must be below the level of the start rule
                    Invariant.Require(this._result == null);
                    this._result = parseNode;
                }
                else
                {
                    // otherwise, we advance the parent node, connecting the advanced node back to the original
                    // parent using the child parse tree. For example if we finished parsing E as E(ID) and the parent
                    // is S -> .E ;, then we would advance the parent to get S -> E .; and connect S -> E .; to S -> .E ;
                    // with E(ID)
                    
                    foreach (var (previousNode, _) in node.PreviousNodes)
                    {
                        if (this.TryReduceToParent(node, parseNode, previousNode))
                        {
                            var toPush = new GraphStructureStackNode(previousNode.Rule.Skip(1), this._index);
                            toPush.AddPrevious(previousNode, parseNode);
                            this.Push(toPush);
                        }
                    }
                }
            }
        }

        private bool TryReduceToParent(GraphStructureStackNode node, InternalParseNode parseNode, GraphStructureStackNode parent)
        {
            if (parent.LastReduction != null)
            {
                Invariant.Require(parent.LastReduction.Width <= parseNode.Width);
                if (parent.LastReduction.Width == parseNode.Width)
                {
                    var comparison = this.ComparePegPrecedence(parseNode, parent.LastReduction);
                    Console.WriteLine($"Ambiguity @{this._index}: {parseNode} vs. {parent.LastReduction} (CHOOSING #{(comparison < 0 ? 1 : 2)})");
                    if (comparison < 0)
                    {
                        // if the new parse is higher precedence, overwrite the original parse
                        parent.LastReduction.SetParse(parseNode.Rule, parseNode.Children);
                    }
                    Invariant.Require(comparison != 0);

                    return false;
                }
            }

            parent.LastReduction = parseNode;
            return true;
        }

        private int ComparePegPrecedence(InternalParseNode a, InternalParseNode b)
        {
            if (a == b)
            {
                return 0;
            }
            if (a.Rule == b.Rule)
            {
                using (var aChildren = a.Children.GetEnumerator())
                using (var bChildren = b.Children.GetEnumerator())
                {
                    while (aChildren.MoveNext())
                    {
                        bChildren.MoveNext();

                        var childComparison = this.ComparePegPrecedence(aChildren.Current, bChildren.Current);
                        if (childComparison != 0) { return childComparison; }
                    }
                }

                return 0;
            }

            return this._ruleIndices[a.Rule].CompareTo(this._ruleIndices[b.Rule]);
        }

        private static bool AddOrMerge(GraphStructureStackNode node, List<GraphStructureStackNode> list)
        {
            foreach (var existing in list)
            {
                if (node.Rule == existing.Rule)
                {
                    // merge nodes by merging their paths
                    foreach (var (previousNode, previousParse) in node.PreviousNodes)
                    {
                        existing.AddPrevious(previousNode, previousParse);
                    }
                    return false;
                }
            }

            list.Add(node);
            return true;
        }
        
        private Token Peek()
        {
            var index = this._index;
            return index == this._tokens.Count 
                ? this._endToken
                : this._tokens[index];
        }

        private void SwapHeadsAndNewHeads()
        {
            var oldHeads = this._heads;
            this._heads = this._newHeads;
            this._newHeads = oldHeads;
        }

        private class GraphStructureStackNode
        {
            private readonly List<(GraphStructureStackNode node, InternalParseNode parse)> _previousNodes = new List<(GraphStructureStackNode node, InternalParseNode parse)>();

            public GraphStructureStackNode(RuleRemainder rule, int position)
            {
                this.Rule = rule;
                this.Position = position;
            }

            public RuleRemainder Rule { get; }
            private int Position { get; } // for debugging

            public InternalParseNode LastReduction { get; set; }

            public IReadOnlyList<(GraphStructureStackNode node, InternalParseNode parse)> PreviousNodes => this._previousNodes;

            public void AddPrevious(GraphStructureStackNode node, InternalParseNode parse)
            {
                Invariant.Require(node.Position + (parse?.Width ?? 0) == this.Position);
                Invariant.Require((parse == null && this.Rule.Start == 0) || parse.Symbol == this.Rule.Rule.Symbols[this.Rule.Start - 1]);

                this._previousNodes.Add((node, parse));
            }

            public override string ToString() => $"{this.Rule.Produced} -> {string.Join(" ", this.Rule.Rule.Symbols.Select((s, i) => i == this.Rule.Start ? "." + s : s.ToString()))}{(this.Rule.Symbols.Count == 0 ? "." : string.Empty)} @{this.Position}";
        }

        private class InternalParseNode
        {
            public InternalParseNode(Token token, int startIndex)
            {
                this.Token = token;
                this.Width = 1;
                this.StartIndex = startIndex;
            }

            public InternalParseNode(Rule rule, ImmutableLinkedList<InternalParseNode> children, int startIndex)
            {
                this.Rule = rule;
                this.Children = children;
                this.Width = children.Sum(n => n.Width);
                this.StartIndex = startIndex;
            }

            public Token Token { get; }
            public Rule Rule { get; private set; }
            public Symbol Symbol => this.Token ?? this.Rule.Produced.As<Symbol>();
            public ImmutableLinkedList<InternalParseNode> Children { get; private set; }
            public int Width { get; }
            public int StartIndex { get; }

            /// <summary>
            /// Overwrites the parse tree for this node
            /// </summary>
            public void SetParse(Rule rule, ImmutableLinkedList<InternalParseNode> children)
            {
                Invariant.Require(rule.Produced == this.Rule.Produced);
                Invariant.Require(this.Width == children.Sum(n => n.Width));

                this.Rule = rule;
                this.Children = children;
            }

            public override string ToString() => this.ToParseNode().ToString();

            public ParseNode ToParseNode()
            {
                var parseStack = new Stack<ParseNode>();
                Parse(this);
                var result = parseStack.Pop();
                Invariant.Require(parseStack.Count == 0 && result.Width == this.Width && result.StartIndex == this.StartIndex);
                return result;

                void Parse(InternalParseNode internalNode)
                {
                    if (internalNode.Token != null)
                    {
                        parseStack.Push(new ParseNode(internalNode.Token, startIndex: internalNode.StartIndex));
                    }
                    else
                    {
                        foreach (var child in internalNode.Children)
                        {
                            Parse(child);
                        }
                        ReduceBy(internalNode.Rule);
                    }
                }

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
                            children[i] = parseStack.Pop();
                        }
                        parseStack.Push(new ParseNode(rule.Produced, children, GetStartIndex()));
                        
                        int GetStartIndex()
                        {
                            if (parseStack.Count == 0) { return 0; }
                            var previous = parseStack.Peek();
                            return previous.StartIndex + previous.Width;
                        }
                    }
                }
            }
        }
    }
}
