using Forelle.Parsing.Preprocessing.LR;
using Forelle.Tests.Parsing.Construction;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Forelle.Parsing;

namespace Forelle.Tests.Parsing
{
    internal class TestingLRParserInterpreter
    {
        private readonly Dictionary<LRClosure, Dictionary<Symbol, LRAction>> _parsingTable;
        private readonly Stack<LRClosure> _stateStack = new Stack<LRClosure>();
        private readonly Stack<ParseNode> _nodeStack = new Stack<ParseNode>();

        public TestingLRParserInterpreter(Dictionary<LRClosure, Dictionary<Symbol, LRAction>> parsingTable)
        {
            this._parsingTable = parsingTable;
        }

        public ParseNode Parse(IReadOnlyList<Token> tokens, NonTerminal symbol)
        {
            this._stateStack.Clear();
            this._nodeStack.Clear();

            var startState = this._parsingTable.Keys.Single(
                c => c.Keys.Any(r => r.Start == 0 && r.Produced.SyntheticInfo is StartSymbolInfo startSymbolInfo && startSymbolInfo.Symbol == symbol)
            );
            this._stateStack.Push(startState);

            tokens = tokens.Concat(startState.Keys.SelectMany(r => r.Symbols).OfType<Token>().Where(t => t.SyntheticInfo is EndSymbolTokenInfo))
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
                        var children = new ParseNode[reduce.Rule.Symbols.Count];
                        for (var i = children.Length - 1; i >= 0; --i)
                        {
                            children[i] = this._nodeStack.Pop();
                            this._stateStack.Pop();
                        };
                        this._nodeStack.Push(new ParseNode(reduce.Rule.Produced, children, children.Length != 0 ? children[0].StartIndex : index));
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
            var endToken = this._nodeStack.Pop();
            Invariant.Require(endToken.Symbol == tokens[tokens.Count - 1]);
            return this._nodeStack.Pop();
        }
    }
}
