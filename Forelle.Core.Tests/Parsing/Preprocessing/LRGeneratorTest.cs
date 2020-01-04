using Forelle.Parsing;
using Forelle.Parsing.Preprocessing.LR;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Forelle.Tests.Parsing.Preprocessing
{
    using static TestGrammar;

    public class LRGeneratorTest
    {
        [Test]
        public void TestReferenceGrammar()
        {
            // Grammar 3.26 from pg. 65 of modern compiler implementation in ML
            var S = new NonTerminal("S");
            var V = new NonTerminal("V");
            var x = new Token("x");
            var equals = new Token("=");
            var eof = Token.CreateSynthetic("$", new EndSymbolTokenInfo(S));
            var startSymbol = NonTerminal.CreateSynthetic("S'", new StartSymbolInfo(eof));
            var rules = new Rules
            {
                { startSymbol, S, eof },
                { S, V, equals, E },
                { S, E },
                { E, V },
                { V, x },
                { V, Times, E },
            };
            var symbols = rules.SelectMany(r => new[] { r.Produced }.Concat(r.Symbols))
                .Distinct()
                .ToDictionary(s => s.Name.TrimStart(Symbol.SyntheticMarker));

            var parsingTable = LRGenerator.Generate(rules.ToLookup(r => r.Produced), FirstFollowCalculator.Create(rules));

            // validate per the reference parsing table
            var s1 = State(
                "S' -> . S $ ?",
                "S -> . V = E $",
                "S -> . E $",
                "E -> . V $",
                "V -> . x $,=",
                "V -> . * E $,=");
            var s2 = State("S' -> S . $ ?");
            var s3 = State("S -> V . = E $", "E -> V . $");
            var s4 = State(
                "S -> V = . E $",
                "E -> . V $",
                "V -> . x $",
                "V -> . * E $");
            var s5 = State("S -> E . $");
            var s6 = State(
                "V -> * . E $,=",
                "E -> . V $,=",
                "V -> . x $,=",
                "V -> . * E $,=");
            var s7 = State("E -> V . $");
            var s8 = State("V -> x . $,=");
            var s9 = State("S -> V = E . $");
            var s10 = State("V -> * E . $,=");
            var s11 = State("V -> x . $");
            var s12 = State("E -> V . $,=");
            var s13 = State(
                "V -> * . E $",
                "E -> . V $",
                "V -> . x $",
                "V -> . * E $");
            var s14 = State("V -> * E . $");
            // technically not in the reference table, but this is what we do instead of emitting an accept currently
            var s15 = State("S' -> S $ . ?");

            parsingTable.Count.ShouldEqual(15, "should not have extra states");

            CheckTableRow(s1, (x, s8), (Times, s6), (S, s2), (E, s5), (V, s3));
            CheckTableRow(s2, (eof, s15)); // note: in the reference table this is accept
            CheckTableRow(s3, (equals, s4), (eof, rules[3]));
            CheckTableRow(s4, (x, s11), (Times, s13), (E, s9), (V, s7));
            CheckTableRow(s5, (eof, rules[2]));
            CheckTableRow(s6, (x, s8), (Times, s6), (E, s10), (V, s12));
            CheckTableRow(s7, (eof, rules[3]));
            CheckTableRow(s8, (equals, rules[4]), (eof, rules[4]));
            CheckTableRow(s9, (eof, rules[1]));
            CheckTableRow(s10, (equals, rules[5]), (eof, rules[5]));
            CheckTableRow(s11, (eof, rules[4]));
            CheckTableRow(s12, (equals, rules[3]), (eof, rules[3]));
            CheckTableRow(s13, (x, s11), (Times, s13), (E, s14), (V, s7));
            CheckTableRow(s14, (eof, rules[5]));
            CheckTableRow(s15); // can't have any entries since any token after EOF is error

            LRClosure State(params string[] itemStrings)
            {
                var closureBuilder = new Dictionary<LRRule, LRLookahead>();
                foreach (var itemString in itemStrings)
                {
                    var elements = itemString.Split(' ');
                    var produced = (NonTerminal)symbols[elements[0]];
                    // skip produced and ->, take all but the last which is lookahead
                    var ruleSymbols = elements.Skip(2).Take(elements.Length - 3).Where(e => e != ".").Select(e => symbols[e]).ToArray();
                    var rule = rules[produced, ruleSymbols];
                    var cursorPosition = Array.IndexOf(elements, ".") - 2; // -2 to account for produced, ->
                    var lookahead = elements[elements.Length - 1].Split(',').Where(t => t != "?").Select(t => (Token)symbols[t]).ToImmutableHashSet();
                    closureBuilder.Add(new LRRule((PotentialParseParentNode)PotentialParseNode.Create(rule).WithCursor(cursorPosition)), new LRLookahead(lookahead));
                }

                var closure = new LRClosure(closureBuilder);
                Assert.IsTrue(parsingTable.ContainsKey(closure));
                return closure;
            }

            void CheckTableRow(LRClosure rowState, params (Symbol symbol, object entry)[] entries)
            {
                var actual = parsingTable[rowState].Select(kvp => (
                    symbol: kvp.Key,
                    entry: kvp.Value switch 
                    { 
                        LRShiftAction shift => shift.Shifted, 
                        LRGotoAction @goto => @goto.Goto,
                        LRReduceAction reduce => reduce.Rule, 
                        _ => (object)kvp.Value 
                    }
                ));
                CollectionAssert.AreEquivalent(actual, entries);
            }
        }
    }
}
