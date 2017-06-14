using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Forelle.Tests.Parsing
{
    using Forelle.Parsing;
    using Medallion.Collections;
    using static TestGrammar;

    public class LeftRecursionRewriterTest
    {
        [Test]
        public void BasicTest()
        {
            var rules = new Rules
            {
                { Exp, Id },
                { Exp, LeftParen, Exp, RightParen },
                { Exp, Minus, Exp },
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
                    "`(* `Exp_0) -> * `Exp_0 { PARSE AS { Exp -> Exp * Exp } }",
                    "`(+ `Exp_1) -> + `Exp_1 { PARSE AS { Exp -> Exp + Exp } }",
                    "`Exp_0 -> ID { PARSE AS { Exp -> ID } }",
                    "`Exp_0 -> ( Exp ) { PARSE AS { Exp -> ( Exp ) } }",
                    "`Exp_0 -> - `Exp_0 { PARSE AS { Exp -> - Exp } }",
                    "`Exp_1 -> `Exp_0 `List<`(* `Exp_0)> { PARSE AS { Exp -> `Exp_0 `List<`(* `Exp_0)> { PARSE AS {} } } }",
                    "`Exp_2 -> `Exp_1 `List<`(+ `Exp_1)> { PARSE AS { Exp -> `Exp_1 `List<`(+ `Exp_1)> { PARSE AS {} } } }",
                    "`List<`(* `Exp_0)> -> `(* `Exp_0) `List<`(* `Exp_0)> { PARSE AS {} }",
                    "`List<`(* `Exp_0)> -> { PARSE AS {} }",
                    "`List<`(+ `Exp_1)> -> `(+ `Exp_1) `List<`(+ `Exp_1)> { PARSE AS {} }",
                    "`List<`(+ `Exp_1)> -> { PARSE AS {} }",
                    "Exp -> `Exp_2 { PARSE AS {} }",
                    "Exp -> `Exp_2 ? Exp : Exp { RIGHT ASSOCIATIVE, PARSE AS { Exp -> Exp ? Exp : Exp { RIGHT ASSOCIATIVE } } }",
                })
                .ShouldEqual(true, "Found: " + string.Join(Environment.NewLine, rewritten));
        }
    }
}
