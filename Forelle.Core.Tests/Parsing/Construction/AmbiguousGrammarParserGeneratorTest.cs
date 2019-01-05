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
                Create(
                    rules[Exp, foo],
                    rules[foo, Id]
                ),
                Create(
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
            CollectionAssert.AreEquivalent(
                actual: errors.Select(TestHelper.StripIndendation),
                expected: new[]
                {
@"Unable to distinguish between the following parse trees for the sequence of symbols [;]:
	B(C(;))
	B(D(;))",

@"Unable to distinguish between the following parse trees upon encountering token ';':
	C()
    ...^
	C(;)
    ..^."
                }
                .Select(TestHelper.StripIndendation)
            );
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
                Create(
                    rules[Exp, term, Minus, Exp],
                    Create(
                        rules[term, LeftParen, Exp, RightParen],
                        LeftParen,
                        Create(
                            rules[Exp, term],
                            rules[term, Id]
                        ),
                        RightParen
                    ),
                    Minus,
                    rules[Exp, term]
                ),
                // cast
                Create(
                    rules[Exp, term],
                    Create(
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
        public void TestDanglingElseAmbiguity()
        {
            var @if = new Token("if");
            var then = new Token("then");
            var @else = new Token("else");

            var rules = new Rules
            {
                { Exp, Id },
                { Exp, @if, Exp, then, Exp },
                { Exp, @if, Exp, then, Exp, @else, Exp },
            };

            var (parser, errors) = ParserGeneratorTest.CreateParser(rules);
            errors.Count.ShouldEqual(1);
            Console.WriteLine(errors[0]);
            errors[0].ShouldEqualIgnoreIndentation(
@"Unable to distinguish between the following parse trees for the sequence of symbols [if Exp then if Exp then Exp else Exp]:
	Exp(if Exp then Exp(if Exp then Exp else Exp))
	Exp(if Exp then Exp(if Exp then Exp) else Exp)"
            );

            var resolution = new AmbiguityResolution(
                Create(
                    rules[Exp, @if, Exp, then, Exp],
                    @if,
                    Exp,
                    then,
                    Create(rules[Exp, @if, Exp, then, Exp, @else, Exp])
                ),
                Create(
                    rules[Exp, @if, Exp, then, Exp, @else, Exp],
                    @if,
                    Exp,
                    then,
                    Create(rules[Exp, @if, Exp, then, Exp]),
                    @else,
                    Exp
                )
            );

            (parser, errors) = ParserGeneratorTest.CreateParser(rules, resolution);
            Assert.IsEmpty(errors);

            // parse the ambiguity
            parser.Parse(new[] { @if, Id, then, @if, Id, then, Id, @else, Id }, Exp);
            ParserGeneratorTest.ToGroupedTokenString(parser.Parsed)
                .ShouldEqual("(if ID then (if ID then ID else ID))");

            // parse the regular rules
            parser.Parse(new[] { @if, Id, then, Id, @else, Id }, Exp);
            ParserGeneratorTest.ToGroupedTokenString(parser.Parsed)
                .ShouldEqual("(if ID then ID else ID)");

            parser.Parse(new[] { @if, Id, then, Id }, Exp);
            ParserGeneratorTest.ToGroupedTokenString(parser.Parsed)
                .ShouldEqual("(if ID then ID)");

            parser.Parse(new[] { @if, @if, Id, then, Id, @else, Id, then, Id }, Exp);
            ParserGeneratorTest.ToGroupedTokenString(parser.Parsed)
                .ShouldEqual("(if (if ID then ID else ID) then ID)");
        }

        [Test]
        public void TestGenericMethodCallAmbiguity()
        {
            // this test replicates the C# ambiguity with generic method calls:
            // f(g<h, i>(j)) could either be invoking g<h, i> passing in j or
            // calling f passing in g<h and i>(j)

            var name = new NonTerminal("Name");
            var argList = new NonTerminal("Args");
            var genericParameters = new NonTerminal("GenPar");
            var cmp = new NonTerminal("Cmp");
            var compared = new NonTerminal("Compared");

            var rules = new Rules
            {
                { Exp, compared },
                { Exp, compared, cmp, compared },

                { compared, Id },
                { compared, LeftParen, Id, RightParen },
                { compared, name, LeftParen, argList, RightParen },

                { cmp, LessThan },
                { cmp, GreaterThan },

                { argList, Exp },
                { argList, Exp, Comma, argList },

                { name, Id },
                { name, Id, LessThan, genericParameters, GreaterThan },

                { genericParameters, Id },
                { genericParameters, Id, Comma, genericParameters },
            };

            var (parser, errors) = ParserGeneratorTest.CreateParser(rules);
            errors.Count.ShouldEqual(1);
            errors[0].ShouldEqualIgnoreIndentation(
@"Unable to distinguish between the following parse trees for the sequence of symbols [ID < ID , ID > ""("" ID "")""]:
    Args(Exp(Compared(ID) Cmp(<) Compared(ID)) , Args(Exp(Compared(ID) Cmp(>) Compared(""("" ID "")""))))
    Args(Exp(Compared(Name(ID < GenPar(ID , GenPar(ID)) >) ""("" Args(Exp(Compared(ID))) "")"")))");

            var resolution = new AmbiguityResolution(
                PotentialParseNodeParser.Parse(@"Args(Exp(Compared(ID) Cmp(<) Compared(ID)) , Args(Exp(Compared(ID) Cmp(>) Compared(""("" ID "")""))))", rules),
                PotentialParseNodeParser.Parse(@"Args(Exp(Compared(Name(ID < GenPar(ID , GenPar(ID)) >) ""("" Args(Exp(Compared(ID))) "")"")))", rules)
            );

            (parser, errors) = ParserGeneratorTest.CreateParser(rules, resolution);
            Assert.IsEmpty(errors);

            parser.Parse(new[] { Id, LeftParen, Id, LessThan, Id, Comma, Id, GreaterThan, LeftParen, Id, RightParen, RightParen }, Exp);
            ParserGeneratorTest.ToGroupedTokenString(parser.Parsed).ShouldEqual("(ID ( ((ID < ID) , (ID > (( ID )))) ))");
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
                @"Unable to distinguish between the following parse trees upon encountering token '+':
	                A()
	                ...^
	                A(+ A +)
	                ..^....."
            );
        }
    }
}
