using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Forelle.Core.Tests
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
            Assert.Throws<ArgumentException>(() => new ExtendedRuleInfo(parserStateRequirements: new[] { default(ParserStateVariableRequirement) }));
            Assert.Throws<ArgumentException>(() => new ExtendedRuleInfo(parserStateActions: new[] { default(ParserStateVariableAction) }));
            Assert.Throws<ArgumentException>(() => new ExtendedRuleInfo(mappedRules: new[] { default(Rule) }));
            Assert.DoesNotThrow(() => new ExtendedRuleInfo());

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
    }
}
