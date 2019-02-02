using Forelle.Tests.Parsing.Construction;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Forelle.Parsing;
using static Forelle.Tests.TestGrammar;
using static Forelle.Parsing.PotentialParseNode;
using Forelle.Tests.Parsing;

namespace Forelle.Tests
{
    /// <summary>
    /// This test file contains tests which we currently expect to fail because the cause of failure is under active development / investigation
    /// </summary>
    public class ExpectedFailureTests
    {
        #region ---- Discriminator Expansion Edge Cases ----
        /// <summary>
        /// This test contains a grammar where none of our basic expansion or 
        /// prefixing methods generates a workable parse
        /// </summary>
        [Test]
        public void ExpectFailure_TestDifferentiablePrefixGrammar()
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

            var (parser, errors) = ParserGeneratorTest.CreateParser2(rules);
            Assert.IsEmpty(errors);

            parser.Parse(new[] { LeftParen, Id, RightParen }, Exp)
                .ToString()
                .ShouldEqual("Exp(A('(' A(ID) ')'))");

            parser.Parse(new[] { LeftParen, QuestionMark, RightParen, Minus }, Stmt)
                .ToString()
                .ShouldEqual("Stmt(B('(' B(?) ')') -)");

            // when we take away these rules, we no longer generate an A | B
            // discriminator. That means that our only option is to continue
            // stripping tokens off A + | B -. This falls apart on the "(" token,
            // since we get A ) + | B ) -, A ) ) +, B ) ) -, ... The length of
            // the rules just keeps growing and yet it is never the case that one
            // of our existing discriminators forms a prefix
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

        /// <summary>
        /// This test contains a grammar where none of our basic expansion or 
        /// prefixing methods generates a workable parse.
        /// 
        /// The difference between this test an <see cref="TestDifferentiablePrefixGrammar"/> is
        /// that in this grammar A and B cannot be differentiated absent context. Therefore,
        /// we cannot generate a discriminator for A | B although we CAN generate a recognizer
        /// </summary>
        [Test]
        public void ExpectFailure_TestNonDifferentiablePrefixGrammar()
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

            //var (parser, errors) = ParserGeneratorTest.CreateParser(rules);
            //Assert.IsEmpty(errors);

            var (parser, errors) = ParserGeneratorTest.CreateParser2(rules);
            Assert.IsEmpty(errors);

            parser.Parse(new[] { LeftParen, LeftParen, Id, RightParen, RightParen, Plus }, Stmt)
                .ToString()
                .ShouldEqual("Stmt(A('(' A('(' A(ID) ')') ')') +)");

            parser.Parse(new[] { LeftParen, Id, RightParen, Minus }, Stmt)
                .ToString()
                .ShouldEqual("Stmt(B('(' B(ID) ')') -)");
        }

        // todo try another variant where A and B are truly non-differentiable out of context (e. g. both have the same
        // rules and the only distinction is in the follow for the Stmt rule
        #endregion

        [Test]
        public void ExpectFailure_TestCastPrecedenceAmbiguity()
        {
            var cast = new NonTerminal("Cast");
            var term = new NonTerminal("Term");

            // we're confused by cast of subtract vs subtract of cast:
            // (x)y-z could be:
            // cast(x, y-z)
            // OR cast(x, y) - z
            var rules = new Rules
            {
                { Exp, term },
                { Exp, term, Minus, Exp },

                { term, Id },
                { term, cast },

                { cast, LeftParen, Id, RightParen, Exp },
                // make cast not an alias (todo shouldn't be needed)
                { A, Plus, cast, Plus }
            };

            var (parser, errors) = ParserGeneratorTest.CreateParser(rules);
            errors.Count.ShouldEqual(1);
            errors[0].ShouldEqualIgnoreIndentation(
@"Unable to distinguish between the following parse trees for the sequence of symbols [""("" ID "")"" Term - Exp]:
    Exp(Term(Cast(""("" ID "")"" Exp(Term - Exp))))
    Exp(Term(Cast(""("" ID "")"" Exp(Term))) - Exp)"
            );

            // make cast bind tighter
            var resolution = new AmbiguityResolution(
                Create(
                    rules[Exp, term, Minus, Exp],
                    Create(
                        rules[term, cast],
                        Create(
                            rules[cast, LeftParen, Id, RightParen, Exp],
                            LeftParen, Id, RightParen, rules[Exp, term]
                        )
                    ),
                    Minus,
                    Exp
                ),
                Create(
                    rules[Exp, term],
                    Create(
                        rules[term, cast],
                        Create(
                            rules[cast, LeftParen, Id, RightParen, Exp],
                            LeftParen, Id, RightParen,
                            rules[Exp, term, Minus, Exp]
                        )
                    )
                )
            );
            (parser, errors) = ParserGeneratorTest.CreateParser(rules, resolution);
            Assert.IsEmpty(errors);

            // TODO: right now this fails to parse!
            // Here's why: above we identify a valid ambiguity between E -> T - E
            // and E -> T because "-" is in the follow of E. This ambiguity relies on a very specific structure
            // for the parsed T (it needs symbols that could be a cast). The problem is that we optimistically
            // do prefix parsing, so we handle parsing E by first parsing T and then going on to parse
            // E -> ... vs. E -> ... - E. Because of this, the ambiguity resolution gets baked into a parser node
            // that isn't at all dependent on what symbols made up the T, leading to it being applied in cases
            // where it shouldn't be (which shuts off other valid parsing paths). In contrast, had we worked through
            // that symbol set via a set of discriminators we'd be in a good place because we'd be applying our
            // ambiguity context to a very specific scenario
            parser.Parse(new[] { Id, Minus, Id }, Exp);

            //parser.Parse(new[] { LeftParen, Id, RightParen, Id, Minus, Id }, Exp);
            //ParserGeneratorTest.ToGroupedTokenString(parser.Parsed)
            //    .ShouldEqual("((( ID ) ID) - ID)");

            // todo what if we resolve the other way?
            // todo would be nice to express resolutions as strings in tests...

            // todo idea: rather than doing lookback to fix, what if we went back and forced a discriminator rather than a prefix for E -> T - E vs. E -> T?
        }

        [Test]
        public void ExpectFailure_LongerLookaheadRequired()
        {
            // this is an interesting case where an LR(2) parser would be fine but we currently fail.
            // Basically, we get a shift/reduce conflict when looking to either extend our list of "+ +"'s
            // upon seeing a "+" (since it could be the "+" from an outer E + E rule). LR(2) could realize that
            // we need to see 2 "+"'s in order to shift. Possibly lifting would solve this case as well
            //
            // Note that this does also 
            // illustrate why "++" is typically a token: without this if you have both prefix and postfix increment operators
            // then a +++++ b could be (a++) + (++b) or ((a++)++) + b or even a + (++(++b))!

            var rules = new Rules
            {
                { Exp, Id },
                { Exp, Exp, Plus, Plus },
                { Exp, Exp, Plus, Exp }
            };

            // todo at least one problem here is that tryspecialize fails on any trailing cursor, but in the case where there's
            // just one specialization this is actually ok!
            var (parser, errors) = ParserGeneratorTest.CreateParser2(rules);
            Assert.IsEmpty(errors);
        }
    }
}
