using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Forelle.Parsing;
using Forelle.Parsing.Preprocessing;

namespace Forelle.Tests.Parsing.Preprocessing
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
                    "`Start<A> -> A `End<A> { PARSE AS {} }",
                    "`Start<B> -> B `End<B> { PARSE AS {} }",
                    "`Start<Exp> -> Exp `End<Exp> { PARSE AS {} }",
                });
        }
    }
}
