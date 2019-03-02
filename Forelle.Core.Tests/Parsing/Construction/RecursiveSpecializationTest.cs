using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static Forelle.Tests.TestGrammar;

namespace Forelle.Tests.Parsing.Construction
{
    public class RecursiveSpecializationTest
    {
        [Test]
        public void RecursiveLifting()
        {
            // this grammar is interesting because in the abstract we cannot parse B -> B() | B(( B ))
            // because ( is in the follow of B. Lifting here doesn't fully solve the problem because
            // we then find ourselves considering A -> ( .B ) ( B ) | A -> B() ( .B ) and once again
            // we find ourselves wanting to parse a B. HOWEVER, if we tried parsing B in the specific lookahead
            // context of this parsing context, we'd find that "(" is no longer in the next set for B -> B() which
            // now makes this parseable

            var rules = new Rules
            {
                { A, B, LeftParen, B, RightParen },
                { B },
                { B, LeftParen, B, RightParen }
            };

            var (parser, errors) = ParserGeneratorTest.CreateParser2(rules);
            Assert.IsEmpty(errors);

            parser.Parse(new[] { LeftParen, LeftParen, RightParen, RightParen }, A)
                .ToString()
                .ShouldEqual("A(B() '(' B('(' B() ')') ')')");
            parser.Parse(new[] { LeftParen, LeftParen, RightParen, RightParen, LeftParen, RightParen }, A)
                .ToString()
                .ShouldEqual("A(B('(' B('(' B() ')') ')') '(' B() ')')");
            Assert.Throws<InvalidOperationException>(() => parser.Parse(new[] { LeftParen, LeftParen, LeftParen, RightParen, RightParen }, A));
        }

        [Test]
        public void DeeperRecursiveLifting()
        {
            // this test is similar to RecursiveLifting, but by more deeply nesting one of the B's inside parens it forces an extra
            // layer of expansions before all nodes in the context become recursive

            var rules = new Rules
            {
                { A, B, LeftParen, LeftParen, B, RightParen, RightParen },
                { B },
                { B, LeftParen, B, RightParen }
            };

            var (parser, errors) = ParserGeneratorTest.CreateParser2(rules);
            Assert.IsEmpty(errors);

            parser.Parse(new[] { LeftParen, LeftParen, RightParen, RightParen, LeftParen, LeftParen, LeftParen, RightParen, RightParen, RightParen }, A)
                .ToString()
                .ShouldEqual("A(B('(' B('(' B() ')') ')') '(' '(' B('(' B() ')') ')' ')')");

            var peg = new TestingGraphPegParserInterpreter(rules);

            peg.Parse(new[] { LeftParen, LeftParen, RightParen, RightParen, LeftParen, LeftParen, LeftParen, RightParen, RightParen, RightParen }, A)
                .ToString()
                .ShouldEqual("A(B('(' B('(' B() ')') ')') '(' '(' B('(' B() ')') ')' ')')");
        }

        [Test]
        public void TestDifferentiablePrefixGrammar()
        {
            var rules = new Rules
            {
                { A, Id },
                { A, LeftParen, A, RightParen },
                { B, QuestionMark },
                { B, LeftParen, B, RightParen },

                // these rules force us to differentiate between A + and B -
                { Stmt, A, Plus },
                { Stmt, B, Minus },

                // these rules force us to differentiate between A and B which
                // can serve as a prefix for the above
                { Exp, A },
                { Exp, B },
            };

            var (parser, errors) = ParserGeneratorTest.CreateParser2(rules);
            Assert.IsEmpty(errors);

            parser.Parse(new[] { LeftParen, Id, RightParen }, Exp)
                .ToString()
                .ShouldEqual("Exp(A('(' A(ID) ')'))");

            parser.Parse(new[] { LeftParen, QuestionMark, RightParen, Minus }, Stmt)
                .ToString()
                .ShouldEqual("Stmt(B('(' B(?) ')') -)");

            // when we take away these rules, we don't have anything to force us to consider A | B
            // directly. Instead, we must figure this out through specialization
            rules.Remove(rules[Exp, A]).ShouldEqual(true);
            rules.Remove(rules[Exp, B]).ShouldEqual(true);

            (parser, errors) = ParserGeneratorTest.CreateParser2(rules);
            Assert.IsEmpty(errors);

            parser.Parse(new[] { LeftParen, LeftParen, QuestionMark, RightParen, RightParen, Minus }, Stmt)
                .ToString()
                .ShouldEqual("Stmt(B('(' B('(' B(?) ')') ')') -)");

            parser.Parse(new[] { LeftParen, LeftParen, LeftParen, Id, RightParen, RightParen, RightParen, Plus }, Stmt)
                .ToString()
                .ShouldEqual("Stmt(A('(' A('(' A('(' A(ID) ')') ')') ')') +)");

            Assert.Throws<InvalidOperationException>(() => parser.Parse(new[] { LeftParen, LeftParen, LeftParen, Id, RightParen, RightParen, RightParen, Minus }, Stmt));
        }

        [Test]
        public void TestNonDifferentiablePrefixGrammar()
        {
            // The difference between this test and TestDifferentiablePrefixGrammar is
            // that in this grammar A and B cannot be differentiated absent context.Therefore,
            // we cannot generate a discriminator for A | B although we CAN generate a recognizer

            var rules = new Rules
            {
                { A, Id },
                { A, LeftParen, A, RightParen },
                { B, Id },
                { B, LeftParen, B, RightParen },

                // these rules force us to generate a discriminator for A + | B -
                { Stmt, A, Plus },
                { Stmt, B, Minus },
            };
            
            var (parser, errors) = ParserGeneratorTest.CreateParser2(rules);
            Assert.IsEmpty(errors);

            parser.Parse(new[] { LeftParen, LeftParen, Id, RightParen, RightParen, Plus }, Stmt)
                .ToString()
                .ShouldEqual("Stmt(A('(' A('(' A(ID) ')') ')') +)");

            parser.Parse(new[] { LeftParen, Id, RightParen, Minus }, Stmt)
                .ToString()
                .ShouldEqual("Stmt(B('(' B(ID) ')') -)");

            var peg = new TestingGraphPegParserInterpreter(rules);

            peg.Parse(new[] { LeftParen, LeftParen, Id, RightParen, RightParen, Plus }, Stmt)
                .ToString()
                .ShouldEqual("Stmt(A('(' A('(' A(ID) ')') ')') +)");

            peg.Parse(new[] { LeftParen, Id, RightParen, Minus }, Stmt)
                .ToString()
                .ShouldEqual("Stmt(B('(' B(ID) ')') -)");
        }

        [Test]
        public void TestDifferentiablePrefixWithNonDifferentiableSuffixGrammar()
        {
            // similar to TestDifferentiablePrefixGrammar, but in this case the suffix
            // after the differentiable prefix is non-differentiable

            var rules = new Rules
            {
                { A, Id },
                { A, LeftParen, A, RightParen },
                { B, QuestionMark },
                { B, LeftParen, B, RightParen },
                
                { Stmt, A, Plus },
                { Stmt, B, Plus }
            };

            var (parser, errors) = ParserGeneratorTest.CreateParser2(rules);
            Assert.IsEmpty(errors);

            parser.Parse(new[] { LeftParen, QuestionMark, RightParen, Plus }, Stmt)
                .ToString()
                .ShouldEqual("Stmt(B('(' B(?) ')') +)");

            parser.Parse(new[] { LeftParen, LeftParen, LeftParen, Id, RightParen, RightParen, RightParen, Plus }, Stmt)
                .ToString()
                .ShouldEqual("Stmt(A('(' A('(' A('(' A(ID) ')') ')') ')') +)");
        }
    }
}
