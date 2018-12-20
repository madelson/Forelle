using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static Forelle.Tests.TestGrammar;

namespace Forelle.Tests.Parsing.Construction
{
    public class DiscriminatorExpansionEdgeCasesTest
    {
        /// <summary>
        /// This test contains a grammar where none of our basic expansion or 
        /// prefixing methods generates a workable parse
        /// </summary>
        [Test]
        public void TestDifferentiablePrefixGrammar()
        {
            var rules = new Rules
            {
                { A, Id },
                { A, LeftParen, A, RightParen },
                { B, QuestionMark },
                { B, LeftParen, B, RightParen },

                // these rules force us to generate a discriminator for A + | B -
                { Stmt, A, Plus },
                { Stmt, B, Minus },

                // these rules force us to generate a discriminator for A | B which
                // can serve as a prefix for the above
                { Exp, A },
                { Exp, B },
            };

            var (parser, errors) = ParserGeneratorTest.CreateParser(rules);
            Assert.IsEmpty(errors);

            // when we take away these rules, we no longer generate an A | B
            // discriminator. That means that our only option is to continue
            // stripping tokens off A + | B -. This falls apart on the "(" token,
            // since we get A ) + | B ) -, A ) ) +, B ) ) -, ... The length of
            // the rules just keeps growing and yet it is never the case that one
            // of our existing discriminators forms a prefix
            rules.Remove(rules[Exp, A]).ShouldEqual(true);
            rules.Remove(rules[Exp, B]).ShouldEqual(true);

            (parser, errors) = ParserGeneratorTest.CreateParser(rules);
            Assert.IsEmpty(errors);
        }

        /// <summary>
        /// This test contains a grammar where none of our basic expansion or 
        /// prefixing methods generates a workable parse.
        /// 
        /// The difference between this test an <see cref="TestDifferentiablePrefixGrammar"/> is
        /// that in this grammar A and B cannot be differentiated absent context. Therefore,
        /// we cannot generate a discriminator for A | B although we CAN generate a recognizer
        /// </summary>
        [Test]
        public void TestNonDifferentiablePrefixGrammar()
        {
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

            var (parser, errors) = ParserGeneratorTest.CreateParser(rules);
            Assert.IsEmpty(errors);
        }

        // todo try another variant where A and B are truly non-differentiable out of context (e. g. both have the same
        // rules and the only distinction is in the follow for the Stmt rule
    }
}
