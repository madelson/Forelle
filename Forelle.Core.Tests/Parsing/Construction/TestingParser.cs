using Forelle.Parsing;
using Medallion.Collections;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
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
        private readonly Dictionary<int, Stack<AmbiguityCheck>> _ambiguityCheckResults = new Dictionary<int, Stack<AmbiguityCheck>>();

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
            this._ambiguityCheckResults.Clear();

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
            List<int> checksAdded = null;
            foreach (var (ambiguityCheck, nonTerminalParsers) in node.AmbiguityChecks)
            {
                if (this.RunAmbiguityCheck(ambiguityCheck, nonTerminalParsers) is int index)
                {
                    (checksAdded ?? (checksAdded = new List<int>())).Add(index);
                }
            }

            var result = this.ParseNoAmbiguityChecks(node);

            if (checksAdded != null)
            {
                foreach (var index in checksAdded)
                {
                    this._ambiguityCheckResults[index].Pop();
                }
            }

            return result;
        }

        private Rule ParseNoAmbiguityChecks(ParserNode node)
        {
            switch (node)
            {
                case ParseRuleNode ruleNode:
                    Log($"PARSE {ruleNode.Rule}");

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
                    Log($"PEEK {nextToken}");

                    return this.Parse(tokenNode.Mapping[nextToken]);
                case ParsePrefixSymbolsNode prefixNode:
                    Log($"PARSE prefix ({prefixNode.Prefix.Count} symbols)");

                    foreach (var prefixElement in prefixNode.Prefix)
                    {
                        if (prefixElement.Token != null) { this.Eat(prefixElement.Token); }
                        else { this.Parse(prefixElement.Node); }
                    }
                    return this.Parse(prefixNode.SuffixNode);
                case GrammarLookaheadNode grammarLookaheadNode:
                    Log($"DISCRIMINATE {string.Join(" | ", grammarLookaheadNode.RuleMapping.Values)}");

                    if (this.IsInLookahead)
                    {
                        this.Eat(grammarLookaheadNode.Token);
                        var ruleUsed = this.Parse(grammarLookaheadNode.DiscriminatorParse);
                        Log($"USED {ruleUsed} SHOULD USE {grammarLookaheadNode.RuleMapping[ruleUsed]}");
                        // TODO this potentially should perform any rule actions (e. g. state variables)
                        return grammarLookaheadNode.RuleMapping[ruleUsed];
                    }
                    else
                    {
                        this._lookaheadIndex = this._index;

                        this.Eat(grammarLookaheadNode.Token);
                        var ruleUsed = this.Parse(grammarLookaheadNode.DiscriminatorParse);
                        Log($"USED {ruleUsed} SHOULD USE {grammarLookaheadNode.RuleMapping[ruleUsed]}");

                        this._lookaheadIndex = -1;

                        var result = this.Parse(grammarLookaheadNode.NodeMapping[ruleUsed]);
                        if (result != grammarLookaheadNode.RuleMapping[ruleUsed])
                        {
                            throw new InvalidOperationException($"sanity check: expected {grammarLookaheadNode.RuleMapping[ruleUsed]}, but was {result}");
                        }
                        return result;
                    }
                case MapResultNode mapResultNode:
                    if (!this.IsInLookahead) { throw new InvalidOperationException($"Encountered {mapResultNode} outside of lookahead"); }
                    var innerResult = this.Parse(mapResultNode.Mapped);
                    return this.Parse(mapResultNode.Mapping[innerResult]);
                case AmbiguityResolutionNode ambiguityResolutionNode:
                    var index = this.IsInLookahead ? this._lookaheadIndex : this._index;
                    if (!this._ambiguityCheckResults.TryGetValue(index, out var checkResults))
                    {
                        throw new InvalidOperationException($"No ambiguity check results found at {index}!");
                    }
                    var orderedCheckResults = checkResults.Where(ambiguityResolutionNode.ChecksToNodes.ContainsKey)
                        .OrderByDescending(c => c.Priority)
                        .ToArray();
                    Log($"FOUND {orderedCheckResults.Length} CHECK RESULTS @{(this.IsInLookahead ? "L" : string.Empty)}{index}; USING {orderedCheckResults[0]}");
                    if (orderedCheckResults.Length == 0) { throw new InvalidOperationException("No relevant check results"); }
                    return this.Parse(ambiguityResolutionNode.ChecksToNodes[orderedCheckResults[0]]);
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
            Log($"EAT {expectedToken} @ {(this.IsInLookahead ? "L" + this._lookaheadIndex : this._index.ToString())}");

            var nextToken = this.Peek();
            if (nextToken != expectedToken)
            {
                throw new InvalidOperationException($"Expected {expectedToken} but found {nextToken} at index {(this.IsInLookahead ? this._lookaheadIndex : this._index)}");
            }

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

        private int? RunAmbiguityCheck(AmbiguityCheck check, IReadOnlyDictionary<NonTerminal, ParserNode> nonTerminalParsers)
        {
            Log($"START AMBIGUITY CHECK {check} @ {(this.IsInLookahead ? "L" + this._lookaheadIndex : this._index.ToString())}");

            var originalLookaheadIndex = this._lookaheadIndex;
            try
            {
                if (!this.IsInLookahead)
                {
                    this._lookaheadIndex = this._index;
                }

                int? indexToMark = null;
                foreach (var leaf in check.Context.Leaves)
                {
                    if (leaf.Symbol is Token token)
                    {
                        if (leaf.CursorPosition == 0)
                        {
                            indexToMark = this._lookaheadIndex;
                        }
                        this.Eat(token);
                    }
                    else
                    {
                        this.Parse(nonTerminalParsers[(NonTerminal)leaf.Symbol]);
                    }
                }

                Log($"MARK INDEX {indexToMark} FOR AMBIGUITY CHECK {check}");
                this._ambiguityCheckResults.GetOrAdd(indexToMark.Value, _ => new Stack<AmbiguityCheck>()).Push(check);
                return indexToMark;
            }
            catch (Exception ex) when (ex is InvalidOperationException || ex is KeyNotFoundException)
            {
                return null;
            }
            finally
            {
                this._lookaheadIndex = originalLookaheadIndex;

                Log($"END AMBIGUITY CHECK {check}");
            }
        }

        //[System.Diagnostics.Conditional("TESTING_PARSER_INSTRUMENTATION")]
        private static void Log(string message) => Console.WriteLine(message);
    }

    internal class SyntaxNode
    {
        public SyntaxNode(Token token)
            : this(token, Empty.Array<SyntaxNode>())
        {
        }

        public SyntaxNode(NonTerminal symbol, IEnumerable<SyntaxNode> children)
            : this((Symbol)symbol, children.ToArray())
        {
        }

        private SyntaxNode(Symbol symbol, IReadOnlyList<SyntaxNode> children)
        {
            this.Symbol = symbol;
            this.Children = children;
        }

        public Symbol Symbol { get; }
        public IReadOnlyList<SyntaxNode> Children { get; }

        public SyntaxNode Flatten(params NonTerminal[] toFlatten)
        {
            var toFlattenSet = new HashSet<Symbol>(toFlatten);

            IReadOnlyList<SyntaxNode> flatten(SyntaxNode node, ImmutableStack<Symbol> context)
            {
                if (toFlattenSet.Contains(node.Symbol) && !context.IsEmpty && context.Peek() == node.Symbol)
                {
                    return node.Children.SelectMany(c => flatten(c, context)).ToArray();
                }

                var newContext = context.Push(node.Symbol);
                var flattenedChildren = node.Children.SelectMany(c => flatten(c, newContext))
                    .ToArray();
                return flattenedChildren.SequenceEqual(node.Children)
                    ? new[] { node }
                    : new[] { new SyntaxNode(node.Symbol, flattenedChildren) };
            }

            return flatten(this, ImmutableStack<Symbol>.Empty).Single();
        }

        public SyntaxNode Inline(params Symbol[] toRemove)
        {
            var toInlineSet = new HashSet<Symbol>(toRemove);

            IReadOnlyList<SyntaxNode> inline(SyntaxNode node)
            {
                if (toInlineSet.Contains(node.Symbol))
                {
                    return node.Children.SelectMany(inline).ToArray();
                }

                var processedChildren = node.Children.SelectMany(inline)
                    .ToArray();
                return processedChildren.SequenceEqual(node.Children)
                    ? new[] { node }
                    : new[] { new SyntaxNode(node.Symbol, processedChildren) };
            }

            return inline(this).SingleOrDefault();
        }

        public override string ToString()
        {
            return this.Children.Count == 0
                ? this.Symbol.ToString()
                : $"{this.Symbol}({string.Join(", ", this.Children)})";
        }
    }
}
