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
        private readonly AmbiguityContextUnifier _unifier;
        
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
            this._unifier = new AmbiguityContextUnifier(rulesByProduced, firstFollowProvider);
        }

        public (AmbiguityCheck[] Checks, string[] Errors) ResolveAmbiguity(IReadOnlyList<RuleRemainder> rules, Token lookaheadToken)
        {
            var contexts = this._contextualizer.GetExpandedAmbiguityContexts(rules, lookaheadToken);

            var checksAndErrors = contexts.Select(c => this.ResolveAmbiguity(rules, c)).ToArray();

            return (
                checksAndErrors.SelectMany(t => t.Checks).ToArray(),
                checksAndErrors.Select(t => t.Error).Where(e => e != null).ToArray()
            );
        }

        private (AmbiguityCheck[] Checks, string Error) ResolveAmbiguity(
            IReadOnlyList<RuleRemainder> rules,
            IReadOnlyDictionary<RuleRemainder, PotentialParseNode> ambiguityContext)
        {
            var unified = this._unifier.TryUnify(rules.Select(r => ambiguityContext[r]).ToArray(), out var unifieds)
                ? rules.Select((rule, index) => (rule, index)).ToDictionary(t => t.rule, t => unifieds[t.index])
                : null;

            var resolutionContexts = rules.ToDictionary(r => r, r => unified?[r] ?? ambiguityContext[r]);
            var resolution = this._ambiguityResolutions.TryGetValue(resolutionContexts.Values, out var match) ? match : null;

            var reverseOrderedRules = rules.OrderByDescending(
                    r => resolution?.OrderedParses.IndexWhere(p => PotentialParseNode.Comparer.Equals(p, resolutionContexts[r]))
                )
                .ToArray();
            var checks = reverseOrderedRules
                .Select((r, index) => new AmbiguityCheck((PotentialParseParentNode)ambiguityContext[r], mappedRule: r, priority: index))
                .ToArray();

            var error = resolution == null
                ? ToAmbiguityError(resolutionContexts, unified: unified != null)
                : null;

            return (checks, error);
        }

        private static string ToAmbiguityError(IReadOnlyDictionary<RuleRemainder, PotentialParseNode> context, bool unified)
        {
            var builder = new StringBuilder("Unable to distinguish between the following parse trees");
            if (unified)
            {
                builder.Append(" for the sequence of symbols [")
                    .Append(string.Join(" ", context.Values.First().Leaves))
                    .Append(']');
            }
            builder.AppendLine(":")
                .AppendLine(string.Join(Environment.NewLine, context.Values.Select(n => $"\t{(unified ? n.ToString() : n.ToMarkedString().Replace(Environment.NewLine, Environment.NewLine + "\t"))}").OrderBy(s => s)));

            return builder.ToString();
        }
    }
}
