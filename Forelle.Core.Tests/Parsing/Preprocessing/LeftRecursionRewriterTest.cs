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
    using Medallion.Collections;
    using static TestGrammar;

    public class LeftRecursionRewriterTest
    {
        [Test]
        public void TestLeftRecursionRewrite()
        {
            var carrot = new Token("^");

            var rules = new Rules
            {
                { Exp, Id },
                { Exp, LeftParen, Exp, RightParen },
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
                    "`(* `Exp_1) -> * `Exp_1 { PARSE AS { Exp -> Exp * Exp } }",
                    "`(+ `Exp_2) -> + `Exp_2 { PARSE AS { Exp -> Exp + Exp } }",
                    "`Exp_0 -> ID { PARSE AS { Exp -> ID } }",
                    "`Exp_0 -> ( Exp ) { PARSE AS { Exp -> ( Exp ) } }",
                    "`Exp_0 -> - `Exp_0 { PARSE AS { Exp -> - Exp } }",
                    "`Exp_1 -> `Exp_0 { PARSE AS { Exp -> `Exp_0 { PARSE AS {} } } }",
                    "`Exp_1 -> `Exp_0 ^ `Exp_1 { RIGHT ASSOCIATIVE, PARSE AS { Exp -> `Exp_0 ^ Exp { RIGHT ASSOCIATIVE, PARSE AS { Exp -> Exp ^ Exp { RIGHT ASSOCIATIVE } } } } }",
                    "`Exp_2 -> `Exp_1 `List<`(* `Exp_1)> { PARSE AS { Exp -> `Exp_1 `List<`(* `Exp_1)> { PARSE AS {} } } }",
                    "`Exp_3 -> `Exp_2 `List<`(+ `Exp_2)> { PARSE AS { Exp -> `Exp_2 `List<`(+ `Exp_2)> { PARSE AS {} } } }",
                    "`List<`(* `Exp_1)> -> `(* `Exp_1) `List<`(* `Exp_1)> { PARSE AS {} }",
                    "`List<`(* `Exp_1)> -> { PARSE AS {} }",
                    "`List<`(+ `Exp_2)> -> `(+ `Exp_2) `List<`(+ `Exp_2)> { PARSE AS {} }",
                    "`List<`(+ `Exp_2)> -> { PARSE AS {} }",
                    "Exp -> `Exp_3 { PARSE AS {} }",
                    "Exp -> `Exp_3 ? Exp : Exp { RIGHT ASSOCIATIVE, PARSE AS { Exp -> Exp ? Exp : Exp { RIGHT ASSOCIATIVE } } }",
                })
                .ShouldEqual(true, "Found: " + string.Join(Environment.NewLine, rewritten));
        }
    }
}
