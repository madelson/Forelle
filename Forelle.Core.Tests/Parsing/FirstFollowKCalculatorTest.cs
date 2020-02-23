using Forelle.Parsing;
using Forelle.Parsing.Preprocessing.LR.V3;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Forelle.Tests.Parsing
{
    public class FirstFollowKCalculatorTest
    {
        // paper referenced in this test is "A New Algorithm To Evaluate Terminal Heads Of Length K"
        // from http://hyacc.sourceforge.net/files/FirstK_APPLC13.pdf

        [Test]
        public void TestExample1FromPaper()
        {
            var S = new NonTerminal("S");
            var N = new NonTerminal("N");
            var M = new NonTerminal("M");
            var s = new Token("s");
            var t = new Token("t");
            var b = new Token("b");
            var c = new Token("c");

            var rules = new Rules
            {
                { S, N, M },
                { N, s, t },
                { M, b, c }
            };

            var firstFollowK = new FirstFollowKCalculator(rules.ToLookup(r => r.Produced));

            for (var k = 0; k <= 4; ++k)
            {
                CollectionAssert.AreEquivalent(
                    actual: ToStrings(firstFollowK.FirstOf(new[] { N, M }, k)),
                    expected: new[] { "stbc".Substring(0, k) },
                    message: $"k = {k}"
                );
            }

            for (var k = 0; k < 2; ++k)
            {
                CollectionAssert.AreEquivalent(
                    actual: ToStrings(firstFollowK.FollowOf(N, k)),
                    expected: new[] { "bc".Substring(0, k) },
                    message: $"k = {k}"
                );
            }
        }

        [Test]
        public void TestExample2FromPaper()
        {
            var S = new NonTerminal("S");
            var N = new NonTerminal("N");
            var M = new NonTerminal("M");
            var L = new NonTerminal("L");
            var s = new Token("s");
            var t = new Token("t");
            var b = new Token("b");
            var c = new Token("c");

            var rules = new Rules
            {
                { S, N, M, L },
                { N, N, s },
                { N },
                { M, M, t },
                { M },
                { L, b, c },
            };

            var firstFollowK = new FirstFollowKCalculator(rules.ToLookup(r => r.Produced));
            var sequence = new[] { N, M, L };

            CollectionAssert.AreEquivalent(
                actual: ToStrings(firstFollowK.FirstOf(sequence, k: 1)),
                expected: new[] { "s", "t", "b" }
            );

            CollectionAssert.AreEquivalent(
                actual: ToStrings(firstFollowK.FirstOf(sequence, k: 2)),
                expected: new[] { "ss", "st", "sb", "tt", "tb", "bc" }
            );

            CollectionAssert.AreEquivalent(
                actual: ToStrings(firstFollowK.FirstOf(sequence, k: 3)),
                expected: new[] { "sss", "sst", "ssb", "stt", "stb", "sbc", "ttt", "ttb", "tbc", "bc" }
            );

            CollectionAssert.AreEquivalent(
                actual: ToStrings(firstFollowK.FirstOf(sequence, k: 4)),
                expected: new[] { "ssss", "ssst", "sssb", "sstt", "sstb", "ssbc", "sttt", "sttb", "stbc", "tttt", "tttb", "ttbc", "tbc", "sbc", "bc" }
            );
        }

        [Test]
        public void TestExample3FromPaper()
        {
            var a = new Token("a");
            var b = new Token("b");
            var X = new NonTerminal("X");
            var Y = new NonTerminal("Y");

            var rules = new Rules
            {
                { X, X, Y},
                { X, a },
                { Y, b },
                { Y },
            };

            var firstFollowK = new FirstFollowKCalculator(rules.ToLookup(r => r.Produced));

            CollectionAssert.AreEquivalent(
                actual: ToStrings(firstFollowK.FirstOf(new[] { X, Y }, k: 1)),
                expected: new[] { "a" }
            );

            CollectionAssert.AreEquivalent(
                actual: ToStrings(firstFollowK.FirstOf(new[] { X, Y }, k: 2)),
                expected: new[] { "a", "ab" }
            );
        }

        [Test]
        public void TestCorrectedExample3FromPaper()
        {
            var a = new Token("a");
            var b = new Token("b");
            var X = new NonTerminal("X");
            var Y = new NonTerminal("Y");

            var rules = new Rules
            {
                // note: the paper shows this as "XY" but I believe that to be a typo
                { X, X, X },
                { X, a },
                { Y, b },
                { Y },
            };

            var firstFollowK = new FirstFollowKCalculator(rules.ToLookup(r => r.Produced));

            CollectionAssert.AreEquivalent(
                actual: ToStrings(firstFollowK.FirstOf(new[] { X, Y }, k: 1)),
                expected: new[] { "a" }
            );

            CollectionAssert.AreEquivalent(
                actual: ToStrings(firstFollowK.FirstOf(new[] { X, Y }, k: 2)),
                expected: new[] { "a", "ab", "aa" }
            );

            CollectionAssert.AreEquivalent(
                actual: ToStrings(firstFollowK.FollowOf(X, 5)),
                expected: new[] { string.Empty, "a", "aa", "aaa", "aaaa", "aaaaa" }
            );

            CollectionAssert.AreEquivalent(
                actual: ToStrings(firstFollowK.NextOf(new[] { X, Y, X }, X, 3)),
                expected: new[] { "aa", "aab", "aba", "aaa" }
            );
        }

        [Test]
        public void TestExample4FromPaper()
        {
            var x = new Token("x");
            var y = new Token("y");
            var z = new Token("z");
            var u = new Token("u");
            var X = new NonTerminal("X");
            var Y = new NonTerminal("Y");
            var Z = new NonTerminal("Z");
            var U = new NonTerminal("U");

            var rules = new Rules
            {
                { X, Y },
                { X, x },
                { X },
                { Y, Z },
                { Y, y },
                { Y },
                { Z, X },
                { Z, z },
                { Z },
                { U, u },
            };

            var firstFollowK = new FirstFollowKCalculator(rules.ToLookup(r => r.Produced));

            CollectionAssert.AreEquivalent(
                actual: ToStrings(firstFollowK.FirstOf(new[] { X, Y, Z, U }, k: 2)),
                expected: new[] { "xy", "yy", "zy", "zz", "zu", "xu", "xz", "yz", "yx", "yu", "zx", "xx", "u" }
            );
        }

        private static IEnumerable<string> ToStrings(IEnumerable<IReadOnlyList<Token>> firstSet) =>
            firstSet.Select(t => string.Join(string.Empty, t));
    }

    public class FirstFollowKCalculatorAsFirstFollowProviderTest : FirstFollowCalculatorTest
    {
        internal override IFirstFollowProvider CreateProvider(IReadOnlyCollection<Rule> rules)
        {
            return base.CreateProvider(rules);
        }

        private class FirstFollowKCalculatorProvider : IFirstFollowProvider
        {
            private readonly FirstFollowKCalculator _firstFollowKCalculator;

            public FirstFollowKCalculatorProvider(IReadOnlyCollection<Rule> rules)
            {
                this._firstFollowKCalculator = new FirstFollowKCalculator(rules.ToLookup(r => r.Produced));
            }

            public ImmutableHashSet<Token> FirstOf(Symbol symbol) => this._firstFollowKCalculator.FirstOf(symbol, k: 1)
                .Select(t => t.SingleOrDefault())
                .ToImmutableHashSet();

            public ImmutableHashSet<Token> FollowOf(Symbol symbol) => this._firstFollowKCalculator.FollowOf(symbol, k: 1)
                .Where(t => t.Count > 0)
                .Select(t => t.Single())
                .ToImmutableHashSet();

            public ImmutableHashSet<Token> FollowOf(Rule rule) => this.FollowOf(rule.Produced);
        }
    }
}
