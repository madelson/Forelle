using Forelle.Parsing.Preprocessing.LR;
using Forelle.Tests.Parsing.Construction;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Forelle.Parsing;
using Forelle.Parsing.Preprocessing;
using NUnit.Framework;

namespace Forelle.Tests.Parsing
{
    internal class TestingLRParserInterpreter
    {
        private readonly Dictionary<LRClosure, Dictionary<Symbol, LRAction>> _parsingTable;
        private readonly Stack<LRClosure> _stateStack = new Stack<LRClosure>();
        private readonly Stack<ParseNode> _nodeStack = new Stack<ParseNode>();

        public TestingLRParserInterpreter(IReadOnlyList<Rule> rules)
            : this(CreateParsingTable(rules))
        {
        }

        public TestingLRParserInterpreter(Dictionary<LRClosure, Dictionary<Symbol, LRAction>> parsingTable)
        {
            if (parsingTable.SelectMany(kvp => kvp.Value.Values).OfType<LRConflictAction>().Any())
            {
                throw new ArgumentException("conflict found", nameof(parsingTable));
            }

            this._parsingTable = parsingTable;
        }

        private static Dictionary<LRClosure, Dictionary<Symbol, LRAction>> CreateParsingTable(IReadOnlyList<Rule> rules)
        {
            var preprocessed = PreprocessGrammar(rules);

            return LRGenerator.Generate(preprocessed.ToLookup(r => r.Produced), FirstFollowCalculator.Create(preprocessed));
        }

        private static List<Rule> PreprocessGrammar(IReadOnlyList<Rule> rules)
        {
            if (!GrammarValidator.Validate(rules, out var validationErrors))
            {
                throw new ArgumentException("Invalid grammar: " + string.Join(Environment.NewLine, validationErrors));
            }

            var (withoutMultipleNullDerivations, multipleNullDerivationErrors) = MultipleNullDerivationRewriter.Rewrite(rules, Array.Empty<AmbiguityResolution>());
            Assert.IsEmpty(multipleNullDerivationErrors);
            var withoutAliases = AliasHelper.InlineAliases(withoutMultipleNullDerivations, AliasHelper.FindAliases(withoutMultipleNullDerivations));
            var withoutLeftRecursion = LeftRecursionRewriter.Rewrite(withoutAliases);

            var withStartSymbols = StartSymbolAdder.AddStartSymbols(withoutLeftRecursion);

            return withStartSymbols;
        }

        public ParseNode Parse(IReadOnlyList<Token> tokens, NonTerminal symbol)
        {
            this._stateStack.Clear();
            this._nodeStack.Clear();

            var startState = this._parsingTable.Keys.Single(
                c => c.Keys.Any(r => r.Node.GetCursorLeafIndex() == 0 && r.Node.Symbol.SyntheticInfo is StartSymbolInfo startSymbolInfo && startSymbolInfo.Symbol == symbol)
            );
            this._stateStack.Push(startState);

            var endToken = startState.Keys.SelectMany(r => r.Node.GetLeaves()).Select(l => l.Symbol).OfType<Token>().Where(t => t.SyntheticInfo is EndSymbolTokenInfo);
            tokens = tokens.Concat(endToken)
                .ToArray();

            var index = 0;
            while (index < tokens.Count)
            {
                var currentState = this._stateStack.Peek();
                var currentToken = tokens[index];
                var action = this._parsingTable[currentState][currentToken];
                switch (action)
                {
                    case LRShiftAction shift:
                        this._nodeStack.Push(new ParseNode(currentToken, index));
                        this._stateStack.Push(shift.Shifted);
                        ++index;
                        break;
                    case LRReduceAction reduce:
                        TestingGraphPegParserInterpreter.ReduceBy(reduce.Rule, this._nodeStack, baseIndex: 0);
                        for (var i = 0; i < reduce.Rule.Symbols.Count; ++i) { this._stateStack.Pop(); }
                        var gotoAction = (LRGotoAction)this._parsingTable[this._stateStack.Peek()][reduce.Rule.Produced];
                        this._stateStack.Push(gotoAction.Goto);
                        break;
                    default:
                        throw new InvalidOperationException("should never get here");
                }
            }

            // Since LR only acts upon seeing a token, we never perform the last reduction and thus are
            // left with the EOF token on top of the stack. That's fine, since we we don't want to construct
            // the start symbol parse node anyway.

            Invariant.Require(this._nodeStack.Count == 2);
            var lastToken = this._nodeStack.Pop();
            Invariant.Require(lastToken.Symbol == tokens[tokens.Count - 1]);
            return this._nodeStack.Pop();
        }
    }
}
