using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Forelle.Tests.Parsing.Preprocessing
{
    using Forelle.Parsing;
    using Forelle.Parsing.Preprocessing;
    using Forelle.Tests.Parsing.Construction;
    using Medallion.Collections;
    using static TestGrammar;

    public class LeftRecursionRewriterTest
    {
        [Test]
        public void TestLeftRecursionRewrite()
        {
            var carrot = new Token("^");
            var plusPlus = new Token("++");

            var rules = new Rules
            {
                { Exp, Id },
                { Exp, LeftParen, Exp, RightParen },
                { Exp, Exp, plusPlus },
                { Exp, Minus, Exp },
                { new Rule(Exp, new Symbol[] { Exp, carrot, Exp }, ExtendedRuleInfo.RightAssociative) },
                { Exp, Exp, Times, Exp },
                { Exp, Exp, Plus, Exp },
                { new Rule(Exp, new Symbol[] { Exp, QuestionMark, Exp, Colon, Exp }, ExtendedRuleInfo.RightAssociative) }
            };

            var rewritten = LeftRecursionRewriter.Rewrite(rules)
                .OrderBy(r => r.Produced.Name) // stable secondary sort for consistency
                .ToList();

            rewritten.SelectMany(r => Traverse.DepthFirst(r, rr => rr.ExtendedInfo.MappedRules ?? Enumerable.Empty<Rule>()))
                .Where(r => r.ExtendedInfo.MappedRules == null)
                .CollectionShouldEqual(rules, "after taking mappings into account, the rules collections should be the same");

            rewritten.Select(r => r.ToString())
                .SequenceEqual(new[]
                {
                    "`(* `Exp_2) -> * `Exp_2 { PARSE AS { Exp -> Exp * Exp } }",
                    "`(+ `Exp_3) -> + `Exp_3 { PARSE AS { Exp -> Exp + Exp } }",
                    "`++ -> ++ { PARSE AS { Exp -> Exp ++ } }",
                    "`Exp_0 -> ID { PARSE AS { Exp -> ID } }",
                    "`Exp_0 -> ( Exp ) { PARSE AS { Exp -> ( Exp ) } }",
                    "`Exp_1 -> `Exp_0 `List<`++> { PARSE AS { Exp -> `Exp_0 `List<`++> { PARSE AS {} } } }",
                    "`Exp_1 -> - `Exp_1 { PARSE AS { Exp -> - Exp } }",
                    "`Exp_2 -> `Exp_1 { PARSE AS { Exp -> `Exp_1 { PARSE AS {} } } }",
                    "`Exp_2 -> `Exp_1 ^ `Exp_2 { RIGHT ASSOCIATIVE, PARSE AS { Exp -> `Exp_1 ^ Exp { RIGHT ASSOCIATIVE, PARSE AS { Exp -> Exp ^ Exp { RIGHT ASSOCIATIVE } } } } }",
                    "`Exp_3 -> `Exp_2 `List<`(* `Exp_2)> { PARSE AS { Exp -> `Exp_2 `List<`(* `Exp_2)> { PARSE AS {} } } }",
                    "`Exp_4 -> `Exp_3 `List<`(+ `Exp_3)> { PARSE AS { Exp -> `Exp_3 `List<`(+ `Exp_3)> { PARSE AS {} } } }",
                    "`List<`(* `Exp_2)> -> `(* `Exp_2) `List<`(* `Exp_2)> { PARSE AS {} }",
                    "`List<`(* `Exp_2)> -> { PARSE AS {} }",
                    "`List<`(+ `Exp_3)> -> `(+ `Exp_3) `List<`(+ `Exp_3)> { PARSE AS {} }",
                    "`List<`(+ `Exp_3)> -> { PARSE AS {} }",
                    "`List<`++> -> `++ `List<`++> { PARSE AS {} }",
                    "`List<`++> -> { PARSE AS {} }",
                    "Exp -> `Exp_4 { PARSE AS {} }",
                    "Exp -> `Exp_4 ? Exp : Exp { RIGHT ASSOCIATIVE, PARSE AS { Exp -> Exp ? Exp : Exp { RIGHT ASSOCIATIVE } } }",
                })
                .ShouldEqual(true, "Found: " + string.Join(Environment.NewLine, rewritten.Select(r => $"\"{r}\",")));

            var (parser, errors) = ParserGeneratorTest.CreateParser(rules);
            Assert.IsEmpty(errors);

            parser.Parse(new[] { Id, Plus, Minus, Id, plusPlus, Plus, Id }, Exp);
            parser.Parsed.ToString().ShouldEqual("Exp(Exp(Exp(ID), +, Exp(-, Exp(Exp(ID), ++))), +, Exp(ID))");
            
            parser.Parse(new[] { Id, carrot, LeftParen, Id, Plus, Id, Times, Id, RightParen, carrot, Minus, Id }, Exp);
            parser.Parsed.ToString().ShouldEqual("Exp(Exp(ID), ^, Exp(Exp((, Exp(Exp(ID), +, Exp(Exp(ID), *, Exp(ID))), )), ^, Exp(-, Exp(ID))))");

            parser.Parse(new[] { Minus, Minus, Id, plusPlus, plusPlus }, Exp);
            parser.Parsed.ToString().ShouldEqual("Exp(-, Exp(-, Exp(Exp(Exp(ID), ++), ++)))");
        }
    }
}
