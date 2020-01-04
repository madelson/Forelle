using Forelle.Parsing;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Forelle.Tests.Parsing
{
    using static TestGrammar;

    public class PotentialParseNodeTest
    {
        [Test]
        public void TestAdvanceCursorErrorHandling()
        {
            Assert.Catch(() => default(PotentialParseNode).AdvanceCursor());
            Assert.Catch(() => PotentialParseNode.Create(Exp).AdvanceCursor());
            Assert.Catch(() => PotentialParseNode.Create(Exp).WithTrailingCursor().AdvanceCursor());
        }

        [Test]
        public void TestAdvanceCursorLeaf()
        {
            var original = PotentialParseNode.Create(Exp).WithCursor(0);
            var advanced = original.AdvanceCursor();
            Assert.IsTrue(PotentialParseNode.Comparer.Equals(original.WithoutCursor(), advanced.WithoutCursor()));
            advanced.CursorPosition.ShouldEqual(1);
        }

        [Test]
        public void TestAdvanceCursorParent()
        {
            var original = PotentialParseNode.Create(new Rule(Exp, LeftParen, Exp, RightParen)).WithCursor(1);
            var advanced = original.AdvanceCursor();
            Assert.IsTrue(PotentialParseNode.Comparer.Equals(original.WithoutCursor(), advanced.WithoutCursor()));
            advanced.CursorPosition.ShouldEqual(2);
        }

        [Test]
        public void TestAdvanceCursorToOuterSibling()
        {
            var original = PotentialParseNode.Create(
                new Rule(Exp, Exp, Exp),
                PotentialParseNode.Create(new Rule(Exp, Id)).WithCursor(0),
                PotentialParseNode.Create(Exp)
            );
            var advanced = original.AdvanceCursor();
            Assert.IsTrue(PotentialParseNode.Comparer.Equals(original.WithoutCursor(), advanced.WithoutCursor()));
            advanced.CursorPosition.ShouldEqual(1);
        }

        [Test]
        public void TestAdvanceCursorToOuterEmptySibling()
        {
            var original = PotentialParseNode.Create(
                new Rule(Exp, Exp, Exp),
                PotentialParseNode.Create(new Rule(Exp, Id)).WithCursor(0),
                PotentialParseNode.Create(new Rule(Exp))
            );
            var advanced = original.AdvanceCursor();
            Assert.IsTrue(PotentialParseNode.Comparer.Equals(original.WithoutCursor(), advanced.WithoutCursor()));
            advanced.CursorPosition.ShouldEqual(2);
        }

        [Test]
        public void TestAdvanceCursorToInnerSibling()
        {
            var original = PotentialParseNode.Create(
                new Rule(Exp, LeftParen, Exp, RightParen),
                PotentialParseNode.Create(LeftParen),
                PotentialParseNode.Create(new Rule(Exp, Plus, Exp)).WithCursor(0),
                PotentialParseNode.Create(RightParen)
            );
            var advanced = original.AdvanceCursor();
            Assert.IsTrue(PotentialParseNode.Comparer.Equals(original.WithoutCursor(), advanced.WithoutCursor()));
            advanced.CursorPosition.ShouldEqual(original.CursorPosition);
            ((PotentialParseParentNode)advanced).Children[1].CursorPosition.ShouldEqual(1);
        }

        [Test]
        public void TestAdvanceCursorPastEmptySibling()
        {
            var original = PotentialParseNode.Create(
                new Rule(Exp, A, B, C),
                PotentialParseNode.Create(A).WithCursor(0),
                PotentialParseNode.Create(new Rule(B)),
                PotentialParseNode.Create(C)
            );
            var advanced = original.AdvanceCursor();
            Assert.IsTrue(PotentialParseNode.Comparer.Equals(original.WithoutCursor(), advanced.WithoutCursor()));
            advanced.CursorPosition.ShouldEqual(2);
        }
    }
}
