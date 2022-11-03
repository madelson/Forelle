using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ForellePlayground.Tests.LRInlining;

internal class TestingParser
{
    private readonly IReadOnlyList<LRState> _states;

    public TestingParser(IReadOnlyList<LRState> states)
    {
        this._states = states;
    }

    public Rule Parse(params Token[] tokens) => this.Parse((IReadOnlyList<Token>)tokens);

    public Rule Parse(IReadOnlyList<Token> tokens)
    {
        Stack<ISymbol> parseStack = new();
        Stack<LRState> stateStack = new();
        stateStack.Push(this._states[0]);

        var i = 0;
        while (i <= tokens.Count)
        {
            var token = i < tokens.Count ? tokens[i] : LRGenerator.Accept;
            var state = stateStack.Peek();

            var actions = state.GetActions(token);
            if (actions.Length != 1)
            {
                throw new ArgumentException($"State {state.Id}, token {token}@{i}: expected 1 action; found {actions.Length}");
            }

            switch (actions[0])
            {
                case Shift { Destination: var destination }:
                    parseStack.Push(token);
                    ++i;
                    stateStack.Push(destination);
                    break;
                case Reduce { Rule: var rule }:
                    ReduceBy(rule);
                    if (token == LRGenerator.Accept) { return (Rule)parseStack.Single(); }
                    var gotoActions = stateStack.Peek().GetActions(rule.Produced);
                    if (gotoActions.Length != 1) { throw new InvalidOperationException($"Multiple goto actions for state {state.Id}, symbol {rule.Produced}"); }
                    stateStack.Push(((Shift)gotoActions[0]).Destination);
                    break;
                default:
                    throw new InvalidOperationException("Unexpected action");
            }
        }

        throw new InvalidOperationException("Should never get here");

        void ReduceBy(Rule rule)
        {
            var children = new ISymbol[rule.Symbols.Length];
            for (var c = children.Length - 1; c >= 0; --c)
            {
                if (rule.Symbols[c] is Rule inlineRule)
                {
                    ReduceBy(inlineRule);
                }
                else
                {
                    // only pop state when we pop a token
                    stateStack.Pop();
                }
                children[c] = parseStack.Pop();
            }
            parseStack.Push(new Rule(rule.Produced, children));
        }
    }
}