﻿using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Forelle.Core.Tests.Parsing
{
    using Forelle.Parsing;
    using static TestGrammar;

    public class AliasHelperTest
    {
        [Test]
        public void TestAliasDetection()
        {
            var rules = new Rules
            {
                { Exp, Id },
                { Exp, BinOp },
                { Exp, UnOp },

                { BinOp, Exp, Plus, Exp },
                { BinOp, Exp, Minus, Exp },

                { UnOp, Minus, Exp },

                { A, B, Id },
                { A, C },

                { B, Exp },
                { B, C },
                { C, Plus },
            };

            var aliases = AliasHelper.FindAliases(rules);
            aliases.Select(kvp => (alias: kvp.Key, aliased: kvp.Value))
                .CollectionShouldEqual(new[] { (alias: BinOp, aliased: Exp), (alias: UnOp, aliased: Exp) });
        }
    }
}
