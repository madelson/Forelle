using Medallion.Collections;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;

namespace Forelle.Parsing.Construction
{
    internal class AmbiguityResolver
    {
        private readonly IReadOnlyDictionary<IReadOnlyCollection<PotentialParseNode>, AmbiguityResolution> _ambiguityResolutions;
        private readonly AmbiguityContextualizer _contextualizer;
        
        public AmbiguityResolver(
            IReadOnlyDictionary<NonTerminal, IReadOnlyList<Rule>> rulesByProduced,
            IReadOnlyList<AmbiguityResolution> ambiguityResolutions,
            ILookup<NonTerminal, DiscriminatorContext> discriminatorContexts,
            IFirstFollowProvider firstFollowProvider)
        {
            // todo need to validate somewhere that this won't fail with dupes
            this._ambiguityResolutions = ambiguityResolutions.ToDictionary(
                r => r.OrderedParses,
                r => r,
                EqualityComparers.GetCollectionComparer(PotentialParseNode.Comparer)
                    .As<IEqualityComparer<IReadOnlyCollection<PotentialParseNode>>>()
            );

            this._contextualizer = new AmbiguityContextualizer(
                rulesByProduced,
                firstFollowProvider,
                discriminatorContexts
            );
        }

        public (RuleRemainder Rule, string Error) ResolveAmbiguity(IReadOnlyList<RuleRemainder> rules, Token lookaheadToken)
        {
            var contexts = this._contextualizer.GetExpandedAmbiguityContexts(rules, lookaheadToken);

            var contextsToResolutions = contexts.ToDictionary(c => c, c => this._ambiguityResolutions.TryGetValue(c.Values, out var resolution) ? resolution : null);

            if (contextsToResolutions.Values.Contains(null))
            {
                return (
                    Rule: rules[0],
                    Error: string.Join(
                        Environment.NewLine + Environment.NewLine,
                        contextsToResolutions.Where(kvp => kvp.Value == null)
                        // todo cleanup
                            .Select(kvp => $"Unable to distinguish between the following parse trees for the sequence of symbols [{string.Join(" ", kvp.Key.Values.First().Leaves)}]:{Environment.NewLine}{string.Join(Environment.NewLine, kvp.Key.Values.Select(n => $"\t{n}"))}")
                    )
                );
            }

            // todo would be nice to use named tuples instead of dictionary
            var preferredRules = contextsToResolutions.Select(kvp => kvp.Key.Single(e => PotentialParseNode.Comparer.Equals(e.Value, kvp.Value.PreferredParse)).Key)
                .ToArray();

            if (preferredRules.Distinct().Count() != 1)
            {
                return (
                    Rule: rules[0],
                    Error: "conflicting ambiguity resolutions TODO cleanup"
                );
            }

            return (Rule: preferredRules[0], Error: null);
        }
    }
}
