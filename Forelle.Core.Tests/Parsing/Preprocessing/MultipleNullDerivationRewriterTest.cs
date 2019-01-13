using Forelle.Parsing;
using Forelle.Parsing.Preprocessing;
using Forelle.Tests.Parsing.Construction;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static Forelle.Tests.TestGrammar;

namespace Forelle.Tests.Parsing.Preprocessing
{
    public class MultipleNullDerivationRewriterTest
    {
        [Test]
        public void TestBasicCase()
        {
            var rules = new Rules
            {
                { A, Id },
                { A, B, C },
                { A },
                { B, Plus },
                { B },
                { C, Times },
                { C },
                { D, B },
                { D, C },
            };

            var (rewritten, errors) = MultipleNullDerivationRewriter.Rewrite(rules, Array.Empty<AmbiguityResolution>());
            CollectionAssert.AreEquivalent(
                errors.Select(TestHelper.StripIndendation),
                new[]
                {
                    @"Unable to decide between multiple parse trees for the empty sequence of symbols, when parsing A:
	                    A(B() C())
	                    A()",
                    @"Unable to decide between multiple parse trees for the empty sequence of symbols, when parsing D:
	                    D(B())
	                    D(C())"
                }
                .Select(TestHelper.StripIndendation)
            );

            var resolutions = new[]
            {
                CreateResolution(rules, "A()", "A(B() C())"),
                CreateResolution(rules, "D(C())", "D(B())")
            };
            (rewritten, errors) = MultipleNullDerivationRewriter.Rewrite(rules, resolutions);
            Assert.IsEmpty(errors);
            CollectionAssert.AreEqual(
                new[]
                {
                    "A -> { PARSE AS { A ->  } }",
                    "A -> `NotNull<A> { PARSE AS {} }",
                    "B -> +",
                    "B -> ",
                    "C -> *",
                    "C -> ",
                    "D -> { PARSE AS { C -> , D -> C } }",
                    "D -> `NotNull<D> { PARSE AS {} }",
                    "`Tuple<+> -> + { PARSE AS { B -> + } }",
                    "`Tuple<> -> { PARSE AS { B ->  } }",
                    "`NotNull<A> -> ID { PARSE AS { A -> ID } }",
                    "`NotNull<A> -> `Tuple<+> C { PARSE AS { A -> B C } }",
                    "`NotNull<A> -> `Tuple<> * { PARSE AS { C -> *, A -> B C } }",
                    "`NotNull<D> -> + { PARSE AS { B -> +, D -> B } }",
                    "`NotNull<D> -> * { PARSE AS { C -> *, D -> C } }",
                },
                rewritten.Select(r => r.ToString()),
                "Found: " + string.Join(Environment.NewLine, rewritten.Select(r => $"\"{r}\","))
            );

            var (parser, parserErrors) = ParserGeneratorTest.CreateParser(rules, resolutions);
            Assert.IsEmpty(parserErrors);

            parser.Parse(Array.Empty<Token>(), A);
            parser.Parsed.ToString().ShouldEqual("A");

            parser.Parse(Array.Empty<Token>(), D);
            parser.Parsed.ToString().ShouldEqual("D(C)");

            parser.Parse(new[] { Plus, Times }, A);
            parser.Parsed.ToString().ShouldEqual("A(B(+), C(*))");

            parser.Parse(new[] { Plus }, D);
            parser.Parsed.ToString().ShouldEqual("D(B(+))");
        }

        /// <summary>
        /// In this grammar, <see cref="A"/> can has multiple null derivations and no non-null derivations
        /// </summary>
        [Test]
        public void TestCanOnlyDeriveNull()
        {
            var rules = new Rules
            {
                { A, B },
                { A, C },
                { B, C, C },
                { C },
            };

            var (rewritten, errors) = MultipleNullDerivationRewriter.Rewrite(rules, Array.Empty<AmbiguityResolution>());
            errors.Count.ShouldEqual(1);
            errors[0].ShouldEqualIgnoreIndentation(@"
                Unable to decide between multiple parse trees for the empty sequence of symbols, when parsing A:
	                A(B(C() C()))
	                A(C())"
            );

            var resolution = CreateResolution(rules, "A(B(C() C()))", "A(C())");
            (rewritten, errors) = MultipleNullDerivationRewriter.Rewrite(rules, new[] { resolution });
            Assert.IsEmpty(errors);
            CollectionAssert.AreEqual(
                new[]
                {
                    "A -> `Tuple<> { PARSE AS { C -> , B -> C C, A -> B } }",
                    "B -> C C",
                    "C -> ",
                    "`Tuple<> -> { PARSE AS { C ->  } }"
                },
                rewritten.Select(r => r.ToString()),
                "Found: " + string.Join(Environment.NewLine, rewritten.Select(r => $"\"{r}\","))
            );   

            var (parser, createParserErrors) = ParserGeneratorTest.CreateParser(rules, resolution);
            Assert.IsEmpty(createParserErrors);

            parser.Parse(Array.Empty<Token>(), A);
            parser.Parsed.ToString().ShouldEqual("A(B(C, C))");
        }
        
        [Test]
        public void TestRightRecursiveNotNullDerivation()
        {
            var rules = new Rules
            {
                { A, B },
                { A, C, A },
                { B, Plus, B },
                { B },
                { C, Minus, C },
                { C },
            };

            var (rewritten, errors) = MultipleNullDerivationRewriter.Rewrite(rules, Array.Empty<AmbiguityResolution>());
            CollectionAssert.AreEquivalent(
                errors.Select(TestHelper.StripIndendation),
                new[]
                {
                    @"Unable to decide between multiple parse trees for the empty sequence of symbols, when parsing A:
	                    A(B())
	                    A(C() A(B()))",
                    "Unable to rewrite rules to remove multiple null derivations without introducing indirect or hidden left-recursion"
                }
                .Select(TestHelper.StripIndendation)
            );
            CollectionAssert.AreEqual(rules, rewritten);
        }

        [Test]
        public void TestLeftRecursiveNullDerivation()
        {
            var rules = new Rules
            {
                { A, B },
                { A, A, C },
                { B, Plus, B },
                { B },
                { C, Minus, C },
                { C },
            };

            var (rewritten, errors) = MultipleNullDerivationRewriter.Rewrite(rules, Array.Empty<AmbiguityResolution>());
            Assert.That(errors, Does.Contain("Unable to rewrite rules to remove multiple null derivations without introducing indirect or hidden left-recursion"));
            CollectionAssert.AreEqual(rules, rewritten);
        }

        private static AmbiguityResolution CreateResolution(Rules rules, params string[] parses)
        {
            return new AmbiguityResolution(parses.Select(p => PotentialParseNodeParser.Parse(p, rules)));
        }
    }
}
