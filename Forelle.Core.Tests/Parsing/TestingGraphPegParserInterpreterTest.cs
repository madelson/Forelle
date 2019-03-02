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

        //[Test]
        //public void TestPegCastUnaryMinus()
        //{
        //    var term = new NonTerminal("Term");

        //    var rules = new Rules
        //    {
        //        { Exp, term },
        //        { Exp, term, Minus, Exp },

        //        { term, Id },
        //        { term, LeftParen, Exp, RightParen },
        //        { term, Minus, term },
        //        { term, LeftParen, Id, RightParen, term }, // cast
        //    };

        //    var peg = new TestingGraphPegParserInterpreter(rules);
        //    peg.Parse(new[] { LeftParen, Id, RightParen, Minus, Id }, Exp)
        //        .ToString()
        //        .ShouldEqual("E(T('(' E(T(ID)) ')') - E(T(ID)))");
        //}
    }
}
