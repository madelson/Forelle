using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Forelle.Tests
{
    using static TestGrammar;

    public class RuleTest
    {
        [Test]
        public void TestRuleArgumentValidation()
        {
            Assert.Throws<ArgumentNullException>(() => new Rule(null, Id));
            Assert.Throws<ArgumentException>(() => new Rule(Exp, default(Token)));
            Assert.Throws<ArgumentNullException>(() => new Rule(Exp, default(Symbol[])));
            Assert.Throws<ArgumentNullException>(() => new Rule(Exp, default(IEnumerable<Symbol>)));
            Assert.DoesNotThrow(() => new Rule(Exp));
        }

        [Test]
        public void TestExtendedRuleInfoArgumentValidation()
        {
            Assert.Throws<ArgumentException>(() => ExtendedRuleInfo.Create(parserStateRequirements: new[] { default(ParserStateVariableRequirement) }));
            Assert.Throws<ArgumentException>(() => ExtendedRuleInfo.Create(parserStateActions: new[] { default(ParserStateVariableAction) }));
            Assert.Throws<ArgumentException>(() => ExtendedRuleInfo.Empty.Update(mappedRules: new[] { default(Rule) }));
            Assert.DoesNotThrow(() => ExtendedRuleInfo.Create());

            Assert.Throws<FormatException>(() => new ParserStateVariableRequirement(null));
            Assert.Throws<FormatException>(() => new ParserStateVariableRequirement(string.Empty));
            Assert.DoesNotThrow(() => new ParserStateVariableRequirement("a"));

            Assert.Throws<FormatException>(() => new ParserStateVariableAction(null, ParserStateVariableActionKind.Pop));
            Assert.Throws<FormatException>(() => new ParserStateVariableAction(string.Empty, ParserStateVariableActionKind.Pop));
            Assert.Throws<ArgumentException>(() => new ParserStateVariableAction("var", (ParserStateVariableActionKind)1000));
            Assert.DoesNotThrow(() => new ParserStateVariableAction("var", ParserStateVariableActionKind.Push));
        }

        [Test]
        public void TestRuleRemainderArgumentValidation()
        {
            Assert.Throws<ArgumentNullException>(() => new RuleRemainder(null, 0));
            var rule = new Rule(Exp, Id, Id);
            Assert.Throws<ArgumentOutOfRangeException>(() => new RuleRemainder(rule, -1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new RuleRemainder(rule, 3));
            Assert.DoesNotThrow(() => new RuleRemainder(rule, 0));
            Assert.DoesNotThrow(() => new RuleRemainder(rule, 1));
        }

        [Test]
        public void TestSkip()
        {
            var rule = new Rule(Exp, Id, Minus, Id);
            for (var i = 0; i <= rule.Symbols.Count; ++i)
            {
                Assert.IsTrue(rule.Skip(i).Symbols.SequenceEqual(rule.Symbols.Skip(i)), $"symbols for remainder {i}");
                Assert.AreSame(rule.Skip(i), rule.Skip(i), $"remainder {i} is cached");
            }

            Assert.AreSame(rule.Skip(2), rule.Skip(1).Skip(1));

            Assert.Throws<ArgumentOutOfRangeException>(() => rule.Skip(-1));
            Assert.Throws<ArgumentOutOfRangeException>(() => rule.Skip(rule.Symbols.Count + 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => rule.Skip(1).Skip(-1));
            Assert.Throws<ArgumentOutOfRangeException>(() => rule.Skip(3).Skip(1));
        }
    }
}
