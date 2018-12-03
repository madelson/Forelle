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
        // todo this test is interesting because it is ambiguous in Exp but not ambiguous in the
        // context of Stmt. Therefore if we had a way to say "Exp doesn't need to be a start symbol"
        // then this grammar should be handled without any help from ambiguity resolution
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
            errors[0].ShouldEqualIgnoreIndentation(
@"Unable to distinguish between the following parse trees for the sequence of symbols [ID]:
	Exp(Bar(ID))
	Exp(Foo(ID))");

            var ambiguityResolution = new AmbiguityResolution(
                PotentialParseNode.Create(
                    rules[Exp, foo],
                    rules[foo, Id]
                ),
                PotentialParseNode.Create(
                    rules[Exp, bar],
                    rules[bar, Id]
                )
            );
            (parser, errors) = ParserGeneratorTest.CreateParser(rules, ambiguityResolution);
            CollectionAssert.IsEmpty(errors);

            parser.Parse(new[] { Id }, Exp);
            parser.Parsed.ToString()
                .ShouldEqual("Exp(Foo(ID))");
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
            Console.WriteLine(string.Join(Environment.NewLine, errors));
            errors.Count.ShouldEqual(3);
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
            errors[0].ShouldEqualIgnoreIndentation(
@"Unable to distinguish between the following parse trees for the sequence of symbols [""("" ID "")"" - Term]:
    Exp(Term(""("" Exp(Term(ID)) "")"") - Exp(Term))
    Exp(Term(""("" ID "")"" Term(- Term)))");

            var ambiguityResolution = new AmbiguityResolution(
                // subtract
                PotentialParseNode.Create(
                    rules[Exp, term, Minus, Exp],
                    PotentialParseNode.Create(
                        rules[term, LeftParen, Exp, RightParen],
                        LeftParen,
                        PotentialParseNode.Create(
                            rules[Exp, term],
                            rules[term, Id]
                        ),
                        RightParen
                    ),
                    Minus,
                    rules[Exp, term]
                ),
                // cast
                PotentialParseNode.Create(
                    rules[Exp, term],
                    PotentialParseNode.Create(
                        rules[term, LeftParen, Id, RightParen, term],
                        LeftParen,
                        Id,
                        RightParen,
                        rules[term, Minus, term]
                    )
                )
            );

            (parser, errors) = ParserGeneratorTest.CreateParser(rules, ambiguityResolution);
            Assert.IsEmpty(errors);

            parser.Parse(new[] { LeftParen, Id, RightParen, Minus, Id }, Exp);
            parser.Parsed.ToString()
                .ShouldEqual("Exp(Term((, Exp(Term(ID)), )), -, Exp(Term(ID)))");
        }

        [Test]
        public void TestCastPrecedenceAmbiguity()
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
                // make cast not an alias
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

            //parser.Parse(new[] { LeftParen, Id, RightParen, Id, Minus, Id }, Exp);
            //ParserGeneratorTest.ToGroupedTokenString(parser.Parsed)
            //    .ShouldEqual("((( ID ) ID) - ID)");
            parser.Parse(new[] { Id, Minus, Id }, Exp);

            // todo what if we resolve the other way?
            // todo would be nice to express resolutions as strings in tests...

            // todo idea: rather than doing lookback to fix, what if we went back and forced a discriminator rather than a prefix for E -> T - E vs. E -> T?
        }

        /// <summary>
        /// This test demonstrates handling of an unambiguous grammar which we can't handle. Because
        /// it's in that class, we're unable to unify the ambiguity contexts we find because there isn't
        /// actually an ambiguity. Therefore all we can do is print out our current state with markers to
        /// show where the parser is when trying to make it's decision
        /// </summary>
        [Test]
        public void TestPalindromePseudoAmbiguity()
        {
            var rules = new Rules
            {
                { A },
                { A, Plus, A, Plus }
            };

            var (parser, errors) = ParserGeneratorTest.CreateParser(rules);
            errors.Count.ShouldEqual(1);
            errors[0].ShouldEqualIgnoreIndentation(
                @"Unable to distinguish between the following parse trees:
	                A(+ A +)
	                ..^.....
	                A(+ A() +)
	                ........^."
            );
        }
    }
}
