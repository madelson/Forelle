using Forelle.Parsing;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static Forelle.Tests.TestGrammar;
using static Forelle.Parsing.PotentialParseNode;

namespace Forelle.Tests.Parsing.Construction
{
    public class AmbiguousGrammarParserGeneratorTest
    {
        [Test]
        public void TestSimpleAmbiguity()
        {
            var foo = new NonTerminal("Foo");
            var bar = new NonTerminal("Bar");

            var rules = new Rules
            {
                { Exp, foo },
                { Exp, bar },

                // make foo/bar not aliases
                { Stmt, foo, Dot },
                { Stmt, bar, SemiColon },

                { foo, Id },
                { bar, Id },
            };

            var (parser, errors) = ParserGeneratorTest.CreateParser(rules);
            errors.Count.ShouldEqual(1);
            throw new NotImplementedException(); // more checks
        }

        [Test]
        public void TestGrammarWhereAllRulesHaveSuffixesAtAmbiguityPoint()
        {
            var rules = new Rules
            {
                { A, B, SemiColon },
                { B, C },
                { C, SemiColon },
                { C },
                { B, D },
                { D, SemiColon },

                // ensure that C, D are not aliases of B
                { A, LeftParen, C, RightParen },
                { B, Plus, D, Minus },
            };

            var (parser, errors) = ParserGeneratorTest.CreateParser(rules);
            errors.Count.ShouldEqual(2);
        }

        /// <summary>
        /// Tests parsing an ambiguous grammar with a casting ambiguity similar to what we have in C#/Java
        /// 
        /// E. g. (x)-y could be casting -y to x or could be subtracting y from x.
        /// </summary>
        [Test]
        public void TestCastUnaryMinusAmbiguity()
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

            var (parser, errors) = ParserGeneratorTest.CreateParser(rules);
            errors.Count.ShouldEqual(1);
            Assert.That(errors[0], Does.Contain("Full context: '( ID ) Term - Exp'"));
        }

        [Test]
        public void TestCastPrecedenceAmbiguity()
        {
            var cast = new NonTerminal("Cast");
            var term = new NonTerminal("Term");
            
            // todo this is the wrong ambiguity. There is no unary minus so
            // we aren't confused about cast of negative vs. subtract. Instead,
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
                // make cast not an alias
                { A, Plus, cast, Plus }
            };

            var (parser, errors) = ParserGeneratorTest.CreateParser(rules);
            errors.Count.ShouldEqual(1);
            errors[0].ShouldEqualIgnoreIndentation(
@"Unable to distinguish between the following parse trees for the sequence of symbols [""("" ID "")"" Term - Exp]:
    Exp(Term(Cast(""("" ID "")"" Exp(Term))) - Exp)
	Cast(""("" ID "")"" Exp(Term - Exp))"
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
                    rules[cast, LeftParen, Id, RightParen, Exp],
                    LeftParen, Id, RightParen,
                    rules[Exp, term, Minus, Exp]
                )
            );
            (parser, errors) = ParserGeneratorTest.CreateParser(rules, resolution);
            Assert.IsEmpty(errors);

            //parser.Parse(new[] { LeftParen, Id, RightParen, Id, Minus, Id }, Exp);
            //ParserGeneratorTest.ToGroupedTokenString(parser.Parsed)
            //    .ShouldEqual("((( ID ) ID) - ID)");
            parser.Parse(new[] { Id, Minus, Id }, Exp);

            // todo what if we resolve the other way?
            // todo would be nice to express resolutions as strings in tests...

            // todo idea: rather than doing lookback to fix, what if we went back and forced a discriminator rather than a prefix for E -> T - E vs. E -> T?
        }
    }
}
