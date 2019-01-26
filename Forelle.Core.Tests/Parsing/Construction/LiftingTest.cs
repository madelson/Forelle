using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static Forelle.Tests.TestGrammar;

namespace Forelle.Tests.Parsing.Construction
{
    public class LiftingTest
    {
        [Test]
        public void SimplestLiftingCase()
        {
            // This test is interesting because it is a very simple non-ambiguous grammar
            // where we get stuck trying to generate a parser node for symbol B, which we
            // cannot do because we don't know what to do when trying to parse B and seeing
            // semicolon in the lookahead. However, when we back up to the rule A -> B ;
            // and inline (lift) B to get A -> ; ; | A -> ;, we can definitely parse this!

            // Note that this grammar isn't LR(1) either, as seen by using the grammar:
            // S' -> A
            // A -> B x
            // B -> x
            // B -> ''
            // on http://jsmachines.sourceforge.net/machines/lr1.html
            // Similarly, the simplified grammar A -> x x | x is LR(1)

            var rules = new Rules
            {
                { A, B, SemiColon },
                { B },
                { B, SemiColon },
            };

            var (parser, errors) = ParserGeneratorTest.CreateParser2(rules);
            Assert.IsEmpty(errors);

            parser.Parse(new[] { SemiColon }, A)
                .ToString()
                .ShouldEqual("A(B() ;)");

            parser.Parse(new[] { SemiColon, SemiColon }, A)
                .ToString()
                .ShouldEqual("A(B(;) ;)");

            Assert.Throws<InvalidOperationException>(() => parser.Parse(new[] { SemiColon, SemiColon, SemiColon }, A));
        }

        [Test]
        public void LiftingWithSymbolsBeforeAndAfterLiftedSymbol()
        {
            var rules = new Rules
            {
                { A, C, B, SemiColon, C },
                { B },
                { B, SemiColon },
                { C, Id },
                { C, Plus, C },
            };

            var (parser, errors) = ParserGeneratorTest.CreateParser2(rules);
            Assert.IsEmpty(errors);

            parser.Parse(new[] { Plus, Id, SemiColon, Plus, Plus, Id }, A)
                .ToString()
                .ShouldEqual("A(C(+ C(ID)) B() ; C(+ C(+ C(ID))))");

            parser.Parse(new[] { Plus, Plus, Id, SemiColon, SemiColon, Id }, A)
                .ToString()
                .ShouldEqual("A(C(+ C(+ C(ID))) B(;) ; C(ID))");
        }

        [Test]
        public void NestedLiftingRequired()
        {
            var rules = new Rules
            {
                { A, B, SemiColon },
                { B, Id },
                { B, C },
                { C },
                { C, SemiColon }
            };

            var (parser, errors) = ParserGeneratorTest.CreateParser2(rules);
            Assert.IsEmpty(errors);

            parser.Parse(new[] { SemiColon }, A)
                .ToString()
                .ShouldEqual("A(B(C()) ;)");

            parser.Parse(new[] { SemiColon, SemiColon }, A)
                .ToString()
                .ShouldEqual("A(B(C(;)) ;)");

            parser.Parse(new[] { Id, SemiColon }, A)
                .ToString()
                .ShouldEqual("A(B(ID) ;)");
        }

        [Test]
        public void LiftingWithRuleMappings()
        {
            var rules = new Rules
            {
                { Exp, Exp, Plus, Exp },
                { Exp, B, SemiColon },
                { B },
                new Rule(B, new[] { SemiColon }, ExtendedRuleInfo.Unmapped)
            };

            var (parser, errors) = ParserGeneratorTest.CreateParser2(rules);
            Assert.IsEmpty(errors);

            parser.Parse(new[] { SemiColon, Plus, SemiColon, SemiColon }, Exp)
                .ToString()
                .ShouldEqual("Exp(Exp(B() ;) + Exp(; ;))");
        }
    }
}
