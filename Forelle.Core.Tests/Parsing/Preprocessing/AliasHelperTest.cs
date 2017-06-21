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

    public class AliasHelperTest
    {
        [Test]
        public void TestAliasDetection()
        {
            var rules = new Rules
            {
                { Exp, Id },
                { Exp, BinOp },
                { Exp, UnOp },

                { BinOp, Exp, Plus, Exp },
                { BinOp, Exp, Minus, Exp },
                { BinOp, D },

                { UnOp, Minus, Exp },

                { A, B, Id },
                { A, C },

                { B, Exp },
                { B, C },
                { C, Plus },

                { D, SemiColon },

                { I, J },
                { J, I },

                { K, K }, // does not count as an alias
            };

            var aliases = AliasHelper.FindAliases(rules);
            aliases.Select(kvp => (alias: kvp.Key, aliased: kvp.Value))
                .CollectionShouldEqual(new[] 
                {
                    (alias: BinOp, aliased: Exp),
                    (alias: UnOp, aliased: Exp),
                    (alias: D, aliased: BinOp),
                    (alias: I, aliased: J),
                    (alias: J, aliased: I)
                });

            AliasHelper.IsAliasOf(BinOp, Exp, aliases).ShouldEqual(true);
            AliasHelper.IsAliasOf(D, Exp, aliases).ShouldEqual(true);
            AliasHelper.IsAliasOf(Exp, BinOp, aliases).ShouldEqual(false);
            AliasHelper.IsAliasOf(I, J, aliases).ShouldEqual(true);
            AliasHelper.IsAliasOf(J, I, aliases).ShouldEqual(true);
        }

        [Test]
        public void TestAliasInlining()
        {
            var rules = new Rules
            {
                { A, Id },
                { A, B },
                { A, C },
                { A, Plus },

                { B, E },
                { B, D },

                { C, LeftParen, E },
                { new Rule(C, new[] { SemiColon }, ExtendedRuleInfo.Create(isRightAssociative: true)) },

                { D, Minus },
                { D },

                { E, RightParen },
            };

            AliasHelper.InlineAliases(rules, Empty.ReadOnlyDictionary<NonTerminal, NonTerminal>())
                .ShouldEqual(rules);

            var inlined = AliasHelper.InlineAliases(
                rules,
                new Dictionary<NonTerminal, NonTerminal>
                {
                    { B, A },
                    { C, A },
                    { D, B },
                }
            );

            inlined.Select(r => r.ToString())
                .SequenceEqual(
                    new[]
                    {
                        "A -> ID",
                        "A -> E { PARSE AS { B -> E, A -> B } }",
                        "A -> - { PARSE AS { B -> - { PARSE AS { D -> -, B -> D } }, A -> B } }",
                        "A -> { PARSE AS { B -> { PARSE AS { D -> , B -> D } }, A -> B } }",
                        "A -> ( E { PARSE AS { C -> ( E, A -> C } }",
                        "A -> ; { RIGHT ASSOCIATIVE, PARSE AS { C -> ; { RIGHT ASSOCIATIVE }, A -> C } }",
                        "A -> +",
                        "E -> )"
                    }
                )
                .ShouldEqual(true, $"Found: {string.Join(Environment.NewLine, inlined)}");
        }
    }
}
