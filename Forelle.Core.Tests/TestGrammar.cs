using Forelle.Parsing;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Forelle.Core.Tests
{
    internal static class TestGrammar
    {
        public static readonly Token Id = new Token("ID"),
            LeftParen = new Token("("),
            RightParen = new Token(")"),
            Plus = new Token("+"),
            Minus = new Token("-"),
            SemiColon = new Token(";"),
            Return = new Token("return"),
            Comma = new Token(",");

        public static readonly NonTerminal Exp = new NonTerminal("Exp"),
            Stmt = new NonTerminal("Stmt"),
            ArgList = new NonTerminal("List<Arg>"),
            BinOp = new NonTerminal("BinOp"),
            UnOp = new NonTerminal("UnOp"),
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
            P = new NonTerminal("P");

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
        public void Add(NonTerminal produced, params Symbol[] symbols)
        {
            this.Add(new Rule(produced, symbols));
        }
    }
}
