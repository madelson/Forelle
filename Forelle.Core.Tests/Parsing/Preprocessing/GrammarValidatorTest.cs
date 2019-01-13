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

            GrammarValidator.Validate(rules, out var errors).ShouldEqual(true, errors != null ? string.Join(Environment.NewLine, errors) : null);
            errors.ShouldEqual(null);
        }

        [Test]
        public void TestInvalidGrammar()
        {
            var rules = new Rules
            {
                { Exp, Id },
                { new Rule(Exp, new[] { Id, Id }, ExtendedRuleInfo.Create(parserStateRequirements: new[] { VariableA.Required, VariableA.NegatedRequired })) },
                { Exp, LeftParen, Exp, RightParen },
                { Stmt, Exp, SemiColon },
                { Stmt, Exp, SemiColon },
                { new NonTerminal(Id.Name), new Token(Id.Name) },
                { Exp, Exp },
                { A, B },
                { B, Stmt, C },
                { C, LeftParen, A, RightParen },
                { Stmt, Id, D },

                { new Rule(J, new[] { Id, Id }, ExtendedRuleInfo.Create(parserStateRequirements: new[] { VariableB.NegatedRequired })) },
                { new Rule(K, new[] { LeftParen }, ExtendedRuleInfo.Create(parserStateActions: new[] { VariableB.Push })) },
                { new Rule(K, new[] { RightParen }, ExtendedRuleInfo.Create(parserStateActions: new[] { VariableB.Pop })) },
                { new Rule(K, new[] { Plus }, ExtendedRuleInfo.Create(parserStateActions: new[] { VariableB.Set })) },

                { new Rule(L, new[] { L, A, L }, ExtendedRuleInfo.Create(isRightAssociative: true)) },
                { new Rule(L, new[] { D, A, L }, ExtendedRuleInfo.Create(isRightAssociative: true)) },
                { new Rule(L, new[] { L, A, D }, ExtendedRuleInfo.Create(isRightAssociative: true)) },
                { new Rule(L, new[] { D }, ExtendedRuleInfo.Create(isRightAssociative: true)) },

                { L, M }, // M alias of L
                { new Rule(M, new Symbol[] { L, Id, L }, ExtendedRuleInfo.Create(isRightAssociative: true)) }, // so this works

                { E, Id },
                { E, F, E }, // hidden
                { E, H, Id }, // indirect
                { E, F, H }, // indirect hidden

                { F }, // F nullable
                { F, Plus },

                { G, H, Plus }, // makes H not an alias of E
                { H },
                { H, E, Plus },

                { N, Id },
                { N, O }, // O aliases N
                { O, N }, // N aliases O
                { O, P }, // P also aliases O, but is not part of the cycle 
                { P, Plus },

                { I, Plus, A }, // I references self-recursive symbol A but is not itself self recursive because A does not reference I

                // Q and R are mutually self-recursive
                { Q, Plus, R },
                { R, Minus, Q },
            };

            GrammarValidator.Validate(rules, out var errors).ShouldEqual(false);
            errors.CollectionShouldEqual(new[] {
                "Multiple symbols found with the same name: 'ID'",
                "Rule Exp -> Exp was of invalid form S -> S",
                "No rules found that produce symbol 'D'",
                "Rule Stmt -> Exp ; was specified multiple times",
                "All rules for symbol 'A' recursively contain 'A'",
                "All rules for symbol 'B' recursively contain 'B'",
                "All rules for symbol 'C' recursively contain 'C'",
                "All rules for symbol 'Q' recursively contain 'Q'",
                "All rules for symbol 'R' recursively contain 'R'",
                "Rule Exp -> ID ID { REQUIRE 'A', REQUIRE !'A' } references variable 'A' multiple times. A rule may contain at most one check or one action for a variable",
                "Parser state variable 'A' is missing the following actions: [PUSH, SET, POP]. Each parser state variable must define [PUSH, SET, POP, REQUIRE]",
                "Rule L -> D { RIGHT ASSOCIATIVE } must have at least two symbols to be a right-associative binary rule",
                "The first symbol of rule L -> D A L { RIGHT ASSOCIATIVE } must be directly recursive in order to be a right-associative binary rule",
                "The last symbol of rule L -> L A D { RIGHT ASSOCIATIVE } must be directly recursive in order to be a right-associative binary rule",
                "Rule E -> F E exhibits hidden left-recursion",
                "Rule E -> H ID exhibits indirect left-recursion along path H -> E +",
                "Rule E -> F H exhibits indirect hidden left-recursion along path H -> E +",
                "Rule H -> E + exhibits indirect left-recursion along path E -> H ID",
                "Invalid recursive cycle found: N -> O -> N"
            });
        }
        
        // verifies a tricky bug in cycle detection
        [Test]
        public void TestValidGrammarWithOneAlias()
        {
            var rules = new Rules
            {
                { Exp, Id },
                { Exp, UnOp },
                { UnOp, Minus, Exp },
                { A, Plus },
            };

            GrammarValidator.Validate(rules, out var errors).ShouldEqual(true, errors != null ? string.Join(Environment.NewLine, errors) : null);
            errors.ShouldEqual(null);
        }
    }
}
