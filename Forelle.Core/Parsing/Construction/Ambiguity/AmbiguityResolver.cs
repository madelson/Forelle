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
            this._unifier = new AmbiguityContextUnifier(rulesByProduced);
        }
        
        public (RuleRemainder Rule, string[] Errors) ResolveAmbiguity(IReadOnlyList<RuleRemainder> rules, Token lookaheadToken)
        {
            var contexts = this._contextualizer.GetAmbiguityContexts(rules, lookaheadToken);

            // todo our approach to handling more than two rules is far from ideal, but it's not clear what we should do. For example, we could
            // try to iteratively unify pairs of rules until all rules are unified. For now, we just take any unification we can find as the single
            // answer

            var rulePairs = Enumerable.Range(0, rules.Count)
                .SelectMany(i => Enumerable.Range(i + 1, rules.Count - i - 1), (i, j) => (first: rules[i], second: rules[j]));
            Dictionary<RuleRemainder, PotentialParseParentNode> context = null;
            var unified = false;
            foreach (var (first, second) in rulePairs)
            {
                if (this._unifier.TryUnify(contexts[first], contexts[second], lookaheadToken, out var unifiedFirst, out var unifiedSecond))
                {
                    context = new Dictionary<RuleRemainder, PotentialParseParentNode>
                    {
                        { first, unifiedFirst },
                        { second, unifiedSecond }
                    };
                    unified = true;
                }
            }
            if (!unified)
            {
                context = contexts.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.First());
            }

            if (this._ambiguityResolutions.TryGetValue(context.Values, out var resolution))
            {
                var preferredRule = context.Single(kvp => PotentialParseNode.Comparer.Equals(kvp.Value, resolution.PreferredParse)).Key;
                return (Rule: preferredRule, Errors: Array.Empty<string>());
            }

            return (
                Rule: rules[0],
                Errors: new[] { ToAmbiguityError(context, unified, lookaheadToken) }
            );
        }

        private static string ToAmbiguityError(IReadOnlyDictionary<RuleRemainder, PotentialParseParentNode> context, bool unified, Token lookahead)
        {
            var builder = new StringBuilder("Unable to distinguish between the following parse trees");
            if (unified)
            {
                builder.Append(" for the sequence of symbols [")
                    .Append(string.Join(" ", context.Values.First().GetLeaves()))
                    .Append(']');
            }
            else
            {
                builder.Append($" upon encountering token '{lookahead}'");
            }
            builder.AppendLine(":")
                .AppendLine(string.Join(Environment.NewLine, context.Values.Select(n => $"\t{(unified ? n.ToString() : n.ToMarkedString().Replace(Environment.NewLine, Environment.NewLine + "\t"))}").OrderBy(s => s)));

            return builder.ToString();
        }
    }
}
