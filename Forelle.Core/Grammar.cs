using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;

namespace Forelle
{
    internal sealed class Grammar
    {
        public Grammar(IEnumerable<Rule> rules)
        {
            if (rules == null) { throw new ArgumentNullException(nameof(rules)); }

            var rulesArray = rules.ToArray();
            if (rulesArray.Contains(null)) { throw new ArgumentException("must not contain null", nameof(rules)); }
        }

        public ImmutableDictionary<NonTerminal, IReadOnlyList<Rule>> RulesByProduced { get; }
    }
}
