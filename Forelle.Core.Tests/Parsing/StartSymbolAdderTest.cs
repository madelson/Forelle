using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Forelle.Parsing;

namespace Forelle.Tests.Parsing
{
    using static TestGrammar;

    public class StartSymbolAdderTest
    {
        [Test]
        public void TestAddStartSymbols()
        {
            var rules = new Rules
            {
                { A, B, Id },
                { B, Plus },
                { Exp, LeftParen, A, RightParen },
            };

            var withStartSymbols = StartSymbolAdder.AddStartSymbols(rules);

            withStartSymbols.Except(rules)
                .Select(r => r.ToString())
                .CollectionShouldEqual(new[]
                {
                    "`Start<A> -> A `End<A>",
                    "`Start<B> -> B `End<B>",
                    "`Start<Exp> -> Exp `End<Exp>",
                });
        }
    }
}
