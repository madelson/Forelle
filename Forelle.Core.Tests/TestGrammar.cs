﻿using Forelle.Parsing;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Forelle.Tests
{
    internal static class TestGrammar
    {
        public static readonly Token Id = new Token("ID"),
            LeftParen = new Token("("),
            RightParen = new Token(")"),
            Plus = new Token("+"),
            Minus = new Token("-"),
            Times = new Token("*"),
            Divide = new Token("/"),
            LessThan = new Token("<"),
            GreaterThan = new Token(">"),
            SemiColon = new Token(";"),
            Colon = new Token(":"),
            QuestionMark = new Token("?"),
            Return = new Token("return"),
            Comma = new Token(","),
            Dot = new Token("."),
            OpenBracket = new Token("["),
            CloseBracket = new Token("]"),
            OpenBrace = new Token("{"),
            CloseBrace = new Token("}"),
            Await = new Token("await");

        public static readonly NonTerminal Exp = new NonTerminal("Exp"),
            ExpList = new NonTerminal("List<Exp>"),
            Stmt = new NonTerminal("Stmt"),
            StmtList = new NonTerminal("List<Stmt>"),
            ArgList = new NonTerminal("List<Arg>"),
            BinOp = new NonTerminal("BinOp"),
            UnOp = new NonTerminal("UnOp"),
            Cmp = new NonTerminal("Cmp"),
            PlusOrMinus = new NonTerminal("+|-"),
            TimesOrDivide = new NonTerminal("*|/"),
            A = new NonTerminal("A"),
            B = new NonTerminal("B"),
            C = new NonTerminal("C"),
            D = new NonTerminal("D"),
            E = new NonTerminal("E"),
            F = new NonTerminal("F"),
            G = new NonTerminal("G"),
            H = new NonTerminal("H"),
            I = new NonTerminal("I"),
            J = new NonTerminal("J"),
            K = new NonTerminal("K"),
            L = new NonTerminal("L"),
            M = new NonTerminal("M"),
            N = new NonTerminal("N"),
            O = new NonTerminal("O"),
            P = new NonTerminal("P"),
            Q = new NonTerminal("Q"),
            R = new NonTerminal("R");

        public static NonTerminal StartOf(NonTerminal symbol, IEnumerable<Rule> rules) => rules.Select(r => r.Produced)
            .First(s => s.SyntheticInfo is StartSymbolInfo i && i.Symbol == symbol);

        public static Token EndOf(NonTerminal symbol, IEnumerable<Rule> rules) => rules.Select(r => r.Symbols.LastOrDefault() as Token)
            .First(s => s?.SyntheticInfo is EndSymbolTokenInfo i && i.Symbol == symbol);

        public static readonly Variable VariableA = new Variable("A"),
            VariableB = new Variable("B");
    }

    internal class Variable
    {
        public Variable(string name)
        {
            this.Name = name;
            this.Push = new ParserStateVariableAction(name, ParserStateVariableActionKind.Push);
            this.Set = new ParserStateVariableAction(name, ParserStateVariableActionKind.Set);
            this.Pop = new ParserStateVariableAction(name, ParserStateVariableActionKind.Pop);
            this.Required = new ParserStateVariableRequirement(name);
            this.NegatedRequired = new ParserStateVariableRequirement(name, requiredValue: false);
        }

        public string Name { get; }

        public ParserStateVariableAction Push { get; }
        public ParserStateVariableAction Set { get; }
        public ParserStateVariableAction Pop { get; }
        public ParserStateVariableRequirement Required { get; }
        public ParserStateVariableRequirement NegatedRequired { get; }
    }

    internal class Rules : Collection<Rule>
    {
        public Rules() { }

        public Rules(IEnumerable<Rule> rules)
            : base(rules.ToList())
        {
        }

        public Rule this[NonTerminal produced, params Symbol[] symbols]
        {
            get => this.Single(r => r.Produced == produced && r.Symbols.SequenceEqual(symbols));
        }

        public void Add(NonTerminal produced, params Symbol[] symbols)
        {
            this.Add(new Rule(produced, symbols));
        }

        public Dictionary<NonTerminal, IReadOnlyList<Rule>> ToRulesByProduced() =>
            this.GroupBy(r => r.Produced).ToDictionary(g => g.Key, g => (IReadOnlyList<Rule>)g.ToArray());
    }
}
