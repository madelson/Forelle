using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Forelle.Core.Tests.Parsing
{
    using Forelle.Parsing;
    using static TestGrammar;

    public class GrammarValidatorTest
    {
        [Test]
        public void TestValidGrammar()
        {
            var rules = new Rules
            {
                { Exp, Id },
                { Exp, LeftParen, Exp, RightParen },
                { Stmt, Exp, SemiColon },
            };

            GrammarValidator.Validate(rules, out var errors).ShouldEqual(true);
            errors.ShouldEqual(null);
        }

        [Test]
        public void TestInvalidGrammar()
        {
            var rules = new Rules
            {
                { Exp, Id },
                { Exp, LeftParen, Exp, RightParen },
                { Stmt, Exp, SemiColon },
                { Stmt, Exp, SemiColon },
                { new NonTerminal(Id.Name), new Token(Id.Name) },
                { Exp, Exp },
                { A, B },
                { B, Stmt, C },
                { C, LeftParen, A, RightParen },
                { Stmt, Id, D },
            };

            GrammarValidator.Validate(rules, out var errors).ShouldEqual(false);
            Assert.IsEmpty(errors.Except(new[] {
                "Multiple symbols found with the same name: 'ID'",
                "Rule Exp -> Exp was of invalid form S -> S",
                "No rules found that produce symbol 'D'",
                "Rule Stmt -> Exp ; was specified multiple times",
                "All rules for symbol 'A' recursively contain 'A'",
                "All rules for symbol 'B' recursively contain 'B'",
                "All rules for symbol 'C' recursively contain 'C'",
            }));
        }
    }
}
