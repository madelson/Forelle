using Forelle.Parsing;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Forelle.Tests
{
    public class SymbolTest
    {
        [Test]
        public void TestArgumentValidation()
        {
            Assert.Throws<ArgumentException>(() => new Token(null));
            Assert.Throws<ArgumentException>(() => new Token(string.Empty));
            Assert.Throws<ArgumentException>(() => new NonTerminal(null));
            Assert.Throws<ArgumentException>(() => new NonTerminal(string.Empty));
            Assert.Throws<FormatException>(() => new Token("`a"));
            Assert.Throws<FormatException>(() => new NonTerminal("`a"));
            Assert.DoesNotThrow(() => new Token("`"));
            Assert.DoesNotThrow(() => new NonTerminal("`"));
        }

        [Test]
        public void TestSynthetic()
        {
            var syntheticToken = Token.CreateSynthetic("a", new EndSymbolTokenInfo(new NonTerminal("a")));
            syntheticToken.Name.ShouldEqual("`a");
            syntheticToken.IsSynthetic.ShouldEqual(true);
        }
    }
}
