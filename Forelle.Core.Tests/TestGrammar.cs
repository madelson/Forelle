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
            SemiColon = new Token(";"),
            Return = new Token("return"),
            Comma = new Token(",");

        public static readonly NonTerminal Exp = new NonTerminal("Exp"),
            Stmt = new NonTerminal("Stmt"),
            ArgList = new NonTerminal("List<Arg>"),
            A = new NonTerminal("A"),
            B = new NonTerminal("B"),
            C = new NonTerminal("C"),
            D = new NonTerminal("D");

        public static NonTerminal StartOf(NonTerminal symbol, IEnumerable<Rule> rules) => rules.Select(r => r.Produced)
            .First(s => s.SyntheticInfo is StartSymbolInfo i && i.Symbol == symbol);

        public static Token EndOf(NonTerminal symbol, IEnumerable<Rule> rules) => rules.Select(r => r.Symbols.LastOrDefault() as Token)
            .First(s => s?.SyntheticInfo is EndSymbolTokenInfo i && i.Symbol == symbol);
    }

    internal class Rules : Collection<Rule>
    {
        public void Add(NonTerminal produced, params Symbol[] symbols)
        {
            this.Add(new Rule(produced, symbols));
        }
    }
}
