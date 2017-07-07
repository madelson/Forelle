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
            var result = helper.TryGatherPostTokenSuffixes(Id, new RuleRemainder(rules[0], start: 0))
                .ShouldNotEqual(null);
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

        /// <summary>
        /// This was a failure in the original implementation of the algorithm because A -> B could be null
        /// using B -> C C and C -> . Furthermore, A -> ID B ID put ID in the follow of B.
        /// 
        /// However, this really shouldn't be a failure because we have A -> B -> C C -> , and ID is NOT in the
        /// follow of A! Therefore if we see ID ahead, we know that the nullable construction is invalid
        /// </summary>
        [Test]
        public void TestTryGatherPostTokenSuffixesSuccessFromFollowElimination()
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
            var result = helper.TryGatherPostTokenSuffixes(Id, new RuleRemainder(rules[0], start: 0))
                .ShouldNotEqual(null);
            var expecteds = new[]
            {
                new Symbol[] { Plus, Id },
                new Symbol[] { Minus, C },
                new Symbol[] { Minus }, // where the first C is nullable
            };
            result.Count.ShouldEqual(expecteds.Length);
            foreach (var expected in expecteds)
            {
                result.Contains(expected).ShouldEqual(true, $"should contain [{string.Join(", ", expected.AsEnumerable())}]");
            }
        }

        [Test]
        public void TestTryGatherPostTokenSuffixesFailure()
        {
            var rules = new Rules
            {
                { Stmt, A, Id }, // puts ID in Follow(A)
                { A, B },
                { A, Id, B, Id },
                { B, C, C },
                { B, Id, Plus, Id },
                { C },
                { C, Id, Minus },
            };

            var helper = new DiscriminatorHelper(rules.ToRulesByProduced(), FirstFollowCalculator.Create(rules));
            // fails because of the nullable construction A(B(C(), C())) B has ID in it's follow
            var result = helper.TryGatherPostTokenSuffixes(Id, new RuleRemainder(rules[1], start: 0))
                .ShouldEqual(null);
        }
    }
}
