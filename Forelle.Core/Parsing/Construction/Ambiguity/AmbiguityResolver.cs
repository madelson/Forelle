using Medallion.Collections;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;

namespace Forelle.Parsing.Construction.Ambiguity
{
    internal class AmbiguityResolver
    {
        private readonly IReadOnlyDictionary<IReadOnlyCollection<PotentialParseNode>, AmbiguityResolution> _ambiguityResolutions;
        private readonly AmbiguityContextualizer _contextualizer;
        private readonly AmbiguityContextUnifier _unifier;
        private readonly AmbiguityContextUnifier2 _unifier2;
        
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
            this._unifier2 = new AmbiguityContextUnifier2(rulesByProduced);
        }

        // TODO AMB clean up, move to ambiguity folder

        public (RuleRemainder Rule, string[] Errors) ResolveAmbiguity(IReadOnlyList<RuleRemainder> rules, Token lookaheadToken)
        {
            var contexts = this._contextualizer.GetExpandedAmbiguityContexts(rules, lookaheadToken);

            // todo instead of requiring this, we need to do a triangle-join of all distinct rule pairs and unify each,
            // then reconcile those unifications
            Invariant.Require(rules.Count == 2, "todo fix");

            Dictionary<RuleRemainder, PotentialParseParentNode> context;
            bool unified;
            if (this._unifier2.TryUnify(contexts[rules[0]], contexts[rules[1]], lookaheadToken, out var unified1, out var unified2))
            {
                context = new Dictionary<RuleRemainder, PotentialParseParentNode>
                {
                    { rules[0], unified1 },
                    { rules[1], unified2 }
                };
                unified = true;
            }
            else
            {
                context = contexts.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.Single()); // todo single
                unified = false;
            }

            if (this._ambiguityResolutions.TryGetValue(context.Values, out var resolution))
            {
                var preferredRule = context.Single(kvp => PotentialParseNode.Comparer.Equals(kvp.Value, resolution.PreferredParse)).Key;
                return (Rule: preferredRule, Errors: Array.Empty<string>());
            }

            return (
                Rule: rules[0],
                Errors: new[] { ToAmbiguityError(context, unified) }
            );

            //var unifiedContexts = contexts.Select(c =>
            //    {
            //        var contextArray = c.ToArray();
            //        return this._unifier.TryUnify(contextArray.Select(kvp => kvp.Value).ToArray(), out var unified)
            //            ? (
            //                context: contextArray.Select((kvp, i) => (rule: kvp.Key, node: unified[i]))
            //                    .ToDictionary(t => t.rule, t => t.node),
            //                unified: true
            //            )
            //            : (context: c, unified: false);
            //    })
            //    .ToArray();

            //var contextsWithResolutions = unifiedContexts
            //    .Select(c => (c.context, c.unified, resolution: this._ambiguityResolutions.TryGetValue(c.context.Values, out var resolution) ? resolution : null))
            //    .ToArray();

            //if (contextsWithResolutions.Any(c => c.resolution == null))
            //{
            //    return (
            //        Rule: rules[0],
            //        Errors: contextsWithResolutions.Select(c => ToAmbiguityError(c.context, c.unified)).ToArray()
            //    );
            //}

            //var preferredRules = contextsWithResolutions.Select(c => c.context.Single(kvp => PotentialParseNode.Comparer.Equals(kvp.Value, c.resolution.PreferredParse)).Key)
            //    .ToArray();

            //if (preferredRules.Distinct().Count() != 1)
            //{
            //    return (
            //        Rule: rules[0],
            //        Errors: new[] { "conflicting ambiguity resolutions TODO cleanup" }
            //    );
            //}

            //return (Rule: preferredRules[0], Errors: Array.Empty<string>());
        }

        private static string ToAmbiguityError(IReadOnlyDictionary<RuleRemainder, PotentialParseParentNode> context, bool unified)
        {
            var builder = new StringBuilder("Unable to distinguish between the following parse trees");
            if (unified)
            {
                builder.Append(" for the sequence of symbols [")
                    .Append(string.Join(" ", context.Values.First().GetLeaves()))
                    .Append(']');
            }
            builder.AppendLine(":")
                .AppendLine(string.Join(Environment.NewLine, context.Values.Select(n => $"\t{(unified ? n.ToString() : n.ToMarkedString().Replace(Environment.NewLine, Environment.NewLine + "\t"))}").OrderBy(s => s)));

            return builder.ToString();
        }
    }
}
