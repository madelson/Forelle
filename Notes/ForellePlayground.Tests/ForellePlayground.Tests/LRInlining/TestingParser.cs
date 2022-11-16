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
        var delayParseStartStateStackCount = int.MaxValue;
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
                    if (token == LRGenerator.Accept) 
                    {
                        return (Rule)parseStack.Single(); 
                    }
                    var gotoActions = stateStack.Peek().GetActions(rule.Produced);
                    if (gotoActions.Length != 1) { throw new InvalidOperationException($"{gotoActions.Length} goto actions for state {state.Id}, symbol {rule.Produced}"); }
                    stateStack.Push(((Shift)gotoActions[0]).Destination);
                    break;
                default:
                    throw new InvalidOperationException("Unexpected action");
            }
        }

        throw new InvalidOperationException("Should never get here");

        void ReduceBy(Rule rule)
        {
            var startingStateStackCount = stateStack.Count;
            // pop the state stack regardless
            for (var i = 0; i < rule.Descendants.Length; ++i) { stateStack.Pop(); }

            // case 1: staying in or entering delay parse mode
            if (startingStateStackCount - rule.Descendants.Length > delayParseStartStateStackCount 
                || rule.ToString().Contains('|'))
            {
                parseStack.Push(rule);
                delayParseStartStateStackCount = Math.Min(delayParseStartStateStackCount, stateStack.Count);
                return;
            }

            // case 2: exiting delay parse mode
            if (startingStateStackCount > delayParseStartStateStackCount)
            {
                delayParseStartStateStackCount = int.MaxValue;
                
                Stack<ISymbol> buffer = new();
                BufferParseStackForReplay(rule);
                while (buffer.TryPop(out var popped))
                {
                    if (popped is Rule poppedRule)
                    {
                        ReduceParseStack(poppedRule);
                    }
                    else
                    {
                        parseStack.Push(popped);
                    }
                }

                void BufferParseStackForReplay(Rule contextRule)
                {
                    for (var i = contextRule.Symbols.Length - 1; i >= 0; --i)
                    {
                        if (contextRule.Symbols[i] is Rule inlineRule)
                        {
                            BufferParseStackForReplay(inlineRule);
                        }
                        else if (contextRule.Symbols[i] is NonTerminal nonTerminal)
                        {
                            var poppedRule = (Rule)parseStack.Pop();
                            if (LRGenerator.MergedRuleMapping.TryGetValue(poppedRule, out var mapping))
                            {
                                poppedRule = mapping[nonTerminal];
                            }
                            buffer.Push(poppedRule);
                            BufferParseStackForReplay(poppedRule);
                        }
                        else
                        {
                            var poppedToken = (Token)parseStack.Pop();
                            Invariant.Require(poppedToken == (Token)contextRule.Symbols[i]);
                            buffer.Push(poppedToken);
                        }
                    }
                }
            }

            ReduceParseStack(rule);

            void ReduceParseStack(Rule rule)
            {
                var children = new ISymbol[rule.Symbols.Length];
                for (var c = children.Length - 1; c >= 0; --c)
                {
                    if (rule.Symbols[c] is Rule inlineRule)
                    {
                        ReduceParseStack(inlineRule);
                    }
                    children[c] = parseStack.Pop();
                }
                parseStack.Push(new Rule(rule.Produced, children));
            }
        }
    }
}