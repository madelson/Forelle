using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Forelle.Parsing;
using Forelle.Parsing.Preprocessing;

namespace Forelle.Tests.Parsing
{
    using static TestGrammar;

    public class FirstFollowCalculatorTest
    {
        [Test]
        public void TestFirstFollow()
        {
            var rules = StartSymbolAdder.AddStartSymbols(new Rules
            {
                { Stmt, Exp, SemiColon },
                { Stmt, Return, Exp, SemiColon },

                { Exp, Id },
                { Exp, LeftParen, Exp, RightParen },
                { Exp, Exp, Plus, Exp },
                { Exp, Id, LeftParen, ArgList, RightParen },

                { ArgList },
                { ArgList, Exp, Comma, ArgList },
                { ArgList, Exp },
            });
            
            var firstFollow = FirstFollowCalculator.Create(rules);

            firstFollow.FirstOf(Stmt).CollectionShouldEqual(new[] { Id, LeftParen, Return, });
            firstFollow.FirstOf(Exp).CollectionShouldEqual(new[] { Id, LeftParen, });
            firstFollow.FirstOf(ArgList).CollectionShouldEqual(new[] { Id, LeftParen, null });
            foreach (var token in rules.SelectMany(r => r.Symbols).OfType<Token>().Distinct())
            {
                firstFollow.FirstOf(token).CollectionShouldEqual(new[] { token });
            }

            firstFollow.FollowOf(Stmt).CollectionShouldEqual(new[] { EndOf(Stmt, rules) });
            firstFollow.FollowOf(Exp).CollectionShouldEqual(new[] { SemiColon, RightParen, Plus, Comma, EndOf(Exp, rules), EndOf(ArgList, rules) });
            firstFollow.FollowOf(ArgList).CollectionShouldEqual(new[] { RightParen, EndOf(ArgList, rules) });
            firstFollow.FollowOf(Id).CollectionShouldEqual(new[] { LeftParen, RightParen, Plus, Comma, SemiColon, EndOf(Exp, rules), EndOf(ArgList, rules) });
            firstFollow.FollowOf(LeftParen).CollectionShouldEqual(new[] { RightParen, LeftParen, Id });
            firstFollow.FollowOf(RightParen).CollectionShouldEqual(new[] { SemiColon, Comma, Plus, RightParen, EndOf(Exp, rules), EndOf(ArgList, rules) });
            firstFollow.FollowOf(Plus).CollectionShouldEqual(new[] { Id, LeftParen });
            firstFollow.FollowOf(Comma).CollectionShouldEqual(new[] { Id, LeftParen, RightParen, EndOf(ArgList, rules) });
            firstFollow.FollowOf(SemiColon).CollectionShouldEqual(new[] { EndOf(Stmt, rules) });
            firstFollow.FollowOf(Return).CollectionShouldEqual(new[] { Id, LeftParen });
            foreach (var startAndEndSymbol in rules.GetAllSymbols().Where(r => r.SyntheticInfo is StartSymbolInfo || r.SyntheticInfo is EndSymbolTokenInfo))
            {
                firstFollow.FollowOf(startAndEndSymbol).CollectionShouldEqual(Enumerable.Empty<Token>());
            }

            foreach (var rule in rules)
            {
                firstFollow.FollowOf(rule).CollectionShouldEqual(firstFollow.FollowOf(rule.Produced));
            }

            rules.GetAllSymbols().Where(firstFollow.IsNullable)
                .CollectionShouldEqual(new[] { ArgList });

            firstFollow.FirstOf(new Symbol[] { ArgList, Return, SemiColon })
                .CollectionShouldEqual(firstFollow.FirstOf(ArgList).RemoveNull().Union(firstFollow.FirstOf(Return)));

            firstFollow.NextOf(rules.Single(r => r.Produced == Exp && r.Symbols.SequenceEqual(new[] { Id })))
                .CollectionShouldEqual(new[] { Id });
            firstFollow.NextOf(rules.Single(r => r.Produced == ArgList && r.Symbols.Count == 0))
                .CollectionShouldEqual(firstFollow.FollowOf(ArgList));

            var argListTailRule = rules.Single(r => r.Produced == ArgList && r.Symbols.SequenceEqual(new Symbol[] { Exp, Comma, ArgList }));
            firstFollow.NextOf(new RuleRemainder(argListTailRule, start: 1))
                .CollectionShouldEqual(new[] { Comma });
            firstFollow.NextOf(new RuleRemainder(argListTailRule, start: 2))
                .CollectionShouldEqual(firstFollow.FirstOf(ArgList).RemoveNull().Union(firstFollow.FollowOf(ArgList)));
            firstFollow.NextOf(new RuleRemainder(argListTailRule, start: 3))
                .CollectionShouldEqual(firstFollow.FollowOf(ArgList));
        }
    }
}
