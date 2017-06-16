using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Forelle.Parsing;
using NUnit.Framework;

namespace Forelle.Tests.Parsing
{
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
