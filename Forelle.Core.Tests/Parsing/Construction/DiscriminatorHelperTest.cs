using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Forelle;
using Forelle.Parsing.Construction;

namespace Forelle.Tests.Parsing.Construction
{
    using Forelle.Parsing;
    using static TestGrammar;

    public class DiscriminatorHelperTest
    {
        [Test]
        public void TestTryGatherPostTokenSuffixesSuccess()
        {
            var rules = new Rules
            {
                { A, B },
                { A, Id, B, Id },
                { B, C, C },
                { B, Id, Plus, Id },
                { C, QuestionMark },
                { C, Id, Minus },
            };
            
            var helper = new DiscriminatorHelper(rules.ToRulesByProduced(), FirstFollowCalculator.Create(rules));
            var result = helper.TryGatherPostTokenSuffixes(Id, new RuleRemainder(rules[0], start: 0));
            var expecteds = new[]
            {
                new Symbol[] { Plus, Id },
                new Symbol[] { Minus, C }
            };
            result.Count.ShouldEqual(expecteds.Length);
            foreach (var expected in expecteds)
            {
                result.Contains(expected).ShouldEqual(true, $"should contain [{string.Join(", ", expected.AsEnumerable())}]");
            }
        }

        // todo this doesn't feel like it should fail, since A -> B does not actually have ID in it's follow set and
        // therefore any nullable construction for A can safely be ignored because we can't actually see that
        [Test]
        public void TestTryGatherPostTokenSuffixesFailure()
        {
            var rules = new Rules
            {
                { A, B },
                { A, Id, B, Id },
                { B, C, C },
                { B, Id, Plus, Id },
                { C },
                { C, Id, Minus },
            };

            var helper = new DiscriminatorHelper(rules.ToRulesByProduced(), FirstFollowCalculator.Create(rules));
            // fails because of the nullable construction A(B(C(), C())) B has ID in it's follow
            helper.TryGatherPostTokenSuffixes(Id, new RuleRemainder(rules[0], start: 0))
                .ShouldEqual(null);
        }
    }
}
