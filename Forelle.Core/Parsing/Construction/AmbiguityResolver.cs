using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Forelle.Parsing.Construction
{
    internal class AmbiguityResolver
    {
        private readonly IReadOnlyDictionary<NonTerminal, IReadOnlyList<Rule>> _rulesByProduced;
        private readonly ILookup<NonTerminal, DiscriminatorContext> _discriminatorContexts;
        private readonly IFirstFollowProvider _firstFollowProvider;

        public AmbiguityResolver(
            IReadOnlyDictionary<NonTerminal, IReadOnlyList<Rule>> rulesByProduced,
            ILookup<NonTerminal, DiscriminatorContext> discriminatorContexts,
            IFirstFollowProvider firstFollowProvider)
        {
            this._rulesByProduced = rulesByProduced;
            this._discriminatorContexts = discriminatorContexts;
            this._firstFollowProvider = firstFollowProvider;
        }

        public (RuleRemainder Rule, string Error) ResolveAmbiguity(IReadOnlyList<RuleRemainder> rules, Token lookaheadToken)
        {
            // todo actually see if the ambiguity has been specified here
            return (rules[0], $"AMBIGUOUS: {string.Join(" vs. ", rules)} on {lookaheadToken}");
        }
    }
}
