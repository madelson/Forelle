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
        }
    }
}
