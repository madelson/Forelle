using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Forelle.Parsing;
using NUnit.Framework;

namespace Forelle.Tests.Parsing.Construction
{
    using Forelle.Parsing.Construction;
    using Forelle.Parsing.Preprocessing;
    using static TestGrammar;

    public class ParserGeneratorTest
    {
        [Test]
        public void TestLL1Grammar()
        {
            var rules = new Rules
            {
                { Exp, Id },
                { Exp, LeftParen, Exp, RightParen },
                { Exp, UnOp },
                { Exp, BinOp },

                { UnOp, PlusOrMinus, Exp },

                { BinOp, Exp, TimesOrDivide, Exp },
                { BinOp, Exp, PlusOrMinus, Exp },

                { new Rule(PlusOrMinus, new[] { Plus }, ExtendedRuleInfo.Unmapped) },
                { new Rule(PlusOrMinus, new[] { Minus }, ExtendedRuleInfo.Unmapped) },

                { new Rule(TimesOrDivide, new[] { Times }, ExtendedRuleInfo.Unmapped) },
                { new Rule(TimesOrDivide, new[] { Divide }, ExtendedRuleInfo.Unmapped) },
            };

            var (parser, errors) = CreateParser(rules);
            Assert.IsEmpty(errors);

            parser.Parse(new[] { LeftParen, Id, RightParen, Plus, Minus, Id, Times, Id }, Exp);
            parser.Parsed.ToString()
                .ShouldEqual("Exp(BinOp(Exp((, Exp(ID), )), +, Exp(BinOp(Exp(UnOp(-, Exp(ID))), *, Exp(ID)))))");
        }

        /// <summary>
        /// A simple grammar that is not LL(1) due to a common set of prefix symbols
        /// in rules for the same non-terminal
        /// </summary>
        [Test]
        public void TestCommonPrefixGrammar()
        {
            var rules = new Rules
            {
                { A, Id, A, B, Plus },
                { A, Id, A, B, Minus },
                { A, Times },
                { B, SemiColon }
            };

            var (parser, errors) = CreateParser(rules);
            Assert.IsEmpty(errors);

            parser.Parse(new[] { Id, Id, Times, SemiColon, Plus, SemiColon, Minus }, A);
            parser.Parsed.ToString()
                .ShouldEqual("A(ID, A(ID, A(*), B(;), +), B(;), -)");
        }

        [Test]
        public void TestExpressionVsStatementListConflict()
        {
            var rules = new Rules
            {
                { Stmt, Exp, SemiColon },
                { Exp, Id },
                { Exp, OpenBracket, ExpList, CloseBracket },
                { Exp, OpenBracket, Stmt, StmtList, CloseBracket },
                { ExpList },
                { ExpList, Exp, ExpList },
                { StmtList },
                { StmtList, Stmt, StmtList }
            };

            var (parser, errors) = CreateParser(rules);
            Assert.IsEmpty(errors);

            // [];
            parser.Parse(new[] { OpenBracket, CloseBracket, SemiColon }, Stmt);
            parser.Parsed.ToString().ShouldEqual("Stmt(Exp([, List<Exp>, ]), ;)");

            // [ [ id; ] [ [] id ] ];
            parser.Parse(new[] { OpenBracket, OpenBracket, Id, SemiColon, CloseBracket, OpenBracket, OpenBracket, CloseBracket, Id, CloseBracket, CloseBracket, SemiColon }, Stmt);
            parser.Parsed.ToString().ShouldEqual("Stmt(Exp([, List<Exp>(Exp([, Stmt(Exp(ID), ;), List<Stmt>, ]), List<Exp>(Exp([, List<Exp>(Exp([, List<Exp>, ]), List<Exp>(Exp(ID), List<Exp>)), ]), List<Exp>)), ]), ;)");
        }

        private static (TestingParser parser, List<string> errors) CreateParser(Rules rules)
        {
            if (!GrammarValidator.Validate(rules, out var validationErrors))
            {
                throw new ArgumentException("Invalid grammar: " + string.Join(Environment.NewLine, validationErrors));
            }

            var withoutAliases = AliasHelper.InlineAliases(rules, AliasHelper.FindAliases(rules));
            var withoutLeftRecursion = LeftRecursionRewriter.Rewrite(withoutAliases);
            var withStartSymbols = StartSymbolAdder.AddStartSymbols(withoutLeftRecursion);

            var (nodes, errors) = ParserGenerator.CreateParser(withStartSymbols);
            return (parser: nodes != null ? new TestingParser(nodes) : null, errors);
        }
    }
}
