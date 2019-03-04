using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static Forelle.Tests.TestGrammar;

namespace Forelle.Tests.Parsing
{
    public class TestingGraphPegParserInterpreterTest
    {
        [Test]
        public void TestPegPalindromeGrammar()
        {
            var x = new Token("x");

            var rules = new Rules
            {
                { A, x, A, x },
                { A, x }
            };
            var peg = new TestingGraphPegParserInterpreter(rules);

            peg.Parse(Enumerable.Repeat(x, 3).ToList(), A)
                .ToString()
                .ShouldEqual("A(x A(x) x)");

            peg.Parse(Enumerable.Repeat(x, 5).ToList(), A)
                .ToString()
                .ShouldEqual("A(x A(x A(x) x) x)");

            peg.Parse(Enumerable.Repeat(x, 7).ToList(), A)
                .ToString()
                .ShouldEqual("A(x A(x A(x A(x) x) x) x)");

            Assert.Throws<InvalidOperationException>(() => peg.Parse(Enumerable.Repeat(x, 6).ToList(), A));
        }

        // from https://cs.stackexchange.com/questions/74648/unambiguous-grammar-but-its-not-lr1
        [Test]
        public void TestPegEvenLengthPalindrome()
        {
            var rules = new Rules
            {
                { A },
                { A, Plus, A, Plus },
                { A, Times, A, Times }
            };

            var peg = new TestingGraphPegParserInterpreter(rules);

            peg.Parse(new[] { Plus, Times, Times, Plus, Plus, Times, Times, Plus }, A)
                .ToString()
                .ShouldEqual("A(+ A(* A(* A(+ A() +) *) *) +)");
        }

        [Test]
        public void TestPegLeftRecursion()
        {
            var rules = new Rules
            {
                { A, A, Plus },
                { A, Id },
            };

            var peg = new TestingGraphPegParserInterpreter(rules);

            peg.Parse(new[] { Id, Plus, Plus, Plus }, A)
                .ToString()
                .ShouldEqual("A(A(A(A(ID) +) +) +)");
        }

        [Test]
        public void TestPegBinaryLeftRecursion()
        {
            var term = new NonTerminal("Term");

            var rules = new Rules
            {
                { Exp, term },
                { Exp, Exp, Plus, term },
                { term, Id },
                { term, LeftParen, Exp, RightParen },
            };

            var peg = new TestingGraphPegParserInterpreter(rules);

            peg.Parse(new[] { Id, Plus, Id, Plus, LeftParen, Id, Plus, Id, RightParen }, Exp)
                .ToString()
                .ShouldEqual("Exp(Exp(Exp(Term(ID)) + Term(ID)) + Term('(' Exp(Exp(Term(ID)) + Term(ID)) ')'))");
        }

        [Test]
        public void TestPegArithmetic()
        {
            var rules = new Rules
            {
                { Exp, Id },
                { Exp, Minus, Exp },
                { Exp, Exp, PlusOrMinus, Exp },
                { Exp, Exp, TimesOrDivide, Exp },
                { PlusOrMinus, Plus },
                { PlusOrMinus, Minus },
                { TimesOrDivide, Times },
                { TimesOrDivide, Divide },
            };

            var peg = new TestingGraphPegParserInterpreter(rules);

            peg.Parse(new[] { Id, Plus, Id, Times, Id }, Exp)
                .ToString()
                .ShouldEqual("Exp(Exp(ID) +|-(+) Exp(Exp(ID) *|/(*) Exp(ID)))");

            peg.Parse(new[] { Id, Times, Id, Plus, Id }, Exp)
                .ToString()
                .ShouldEqual("Exp(Exp(Exp(ID) *|/(*) Exp(ID)) +|-(+) Exp(ID))");

            peg.Parse(new[] { Id, Times, Id, Minus, Id, Divide, Id }, Exp)
                .ToString()
                .ShouldEqual("Exp(Exp(Exp(ID) *|/(*) Exp(ID)) +|-(-) Exp(Exp(ID) *|/(/) Exp(ID)))");

            peg.Parse(new[] { Id, Times, Id, Minus, Id, Divide, Id, Plus, Id }, Exp)
                .ToString()
                .ShouldEqual("Exp(Exp(Exp(Exp(ID) *|/(*) Exp(ID)) +|-(-) Exp(Exp(ID) *|/(/) Exp(ID))) +|-(+) Exp(ID))");
        }

        [Test]
        public void TestPegAssociativity()
        {
            var rules = new Rules
            {
                { Exp, Exp, Plus, Exp },
                { Exp, Id },
                { Exp, Exp, QuestionMark, Exp, Colon, Exp },
            };

            var peg = new TestingGraphPegParserInterpreter(rules);

            peg.Parse(new[] { Id, Plus, Id, Plus, Id, Plus, Id }, Exp)
                .ToString()
                .ShouldEqual("Exp(Exp(Exp(Exp(ID) + Exp(ID)) + Exp(ID)) + Exp(ID))");

            peg.Parse(new[] { Id, QuestionMark, Id, Colon, Id, QuestionMark, Id, Colon, Id }, Exp)
                .ToString()
                .ShouldEqual("Exp(Exp(ID) ? Exp(ID) : Exp(Exp(ID) ? Exp(ID) : Exp(ID)))");

            // this doesn't behave as we'd want, as here right associative rules are given highest precedence
            peg.Parse(new[] { Id, QuestionMark, Id, Colon, Id, Plus, Id }, Exp)
                .ToString()
                .ShouldEqual("Exp(Exp(Exp(ID) ? Exp(ID) : Exp(ID)) + Exp(ID))");
        }

        [Test]
        public void TestPegCastUnaryMinus()
        {
            var term = new NonTerminal("Term");

            var rules = new Rules
            {
                { Exp, term },
                { Exp, term, Minus, Exp },

                { term, Id },
                { term, LeftParen, Exp, RightParen },
                { term, Minus, term },
                { term, LeftParen, Id, RightParen, term }, // cast
            };

            var peg = new TestingGraphPegParserInterpreter(rules);

            peg.Parse(new[] { LeftParen, Id, RightParen, Minus, Id }, Exp)
                .ToString()
                .ShouldEqual("Exp(Term('(' ID ')' Term(- Term(ID))))");

            rules.Remove(rules[Exp, term, Minus, Exp]);
            rules.Insert(0, new Rule(Exp, term, Minus, Exp));
            peg = new TestingGraphPegParserInterpreter(rules);

            peg.Parse(new[] { LeftParen, Id, RightParen, Minus, Id }, Exp)
                .ToString()
                .ShouldEqual("Exp(Term('(' Exp(Term(ID)) ')') - Exp(Term(ID)))");
        }

        /// <summary>
        /// This grammar is interesting for our PEG ambiguity resolution approach because the
        /// ambiguity occurs deep in the ambiguous parse tree and therefore simply comparing the top
        /// level rule precedence is not sufficient
        /// 
        /// A(+ B(-) B(ID ID) B() + B(-))
        /// A(+ B(-) B() B(ID ID) + B(-))
        /// </summary>
        [Test]
        public void TestPegDeepAmbiguity()
        {
            var rules = new Rules
            {
                { A, Plus, B, B, B, Plus, B },
                { B, Minus },
                { B, Id, Id },
                { B }
            };

            var peg = new TestingGraphPegParserInterpreter(rules);
            peg.Parse(new[] { Plus, Minus, Id, Id, Plus, Minus }, A)
                .ToString()
                .ShouldEqual("A(+ B(-) B(ID ID) B() + B(-))");

            rules.Remove(rules[B, Id, Id]);
            rules.Add(B, Id, Id);
            peg = new TestingGraphPegParserInterpreter(rules);
            peg.Parse(new[] { Plus, Minus, Id, Id, Plus, Minus }, A)
                .ToString()
                .ShouldEqual("A(+ B(-) B() B(ID ID) + B(-))");
        }
    }
}
