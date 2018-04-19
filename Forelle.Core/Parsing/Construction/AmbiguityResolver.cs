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
        private readonly IReadOnlyDictionary<NonTerminal, IReadOnlyList<Rule>> _rulesByProduced;
        private readonly IReadOnlyList<AmbiguityResolution> _ambiguityResolutions;
        private readonly ILookup<NonTerminal, DiscriminatorContext> _discriminatorContexts;
        private readonly IFirstFollowProvider _firstFollowProvider;

        /// <summary>
        /// Maps all <see cref="Symbol"/>s to the set of non-discriminator rules where they are referenced and
        /// the index positions of those references in the rule
        /// </summary>
        private readonly Lazy<ILookup<Symbol, (Rule rule, int index)>> _nonDiscriminatorSymbolReferences;

        public AmbiguityResolver(
            IReadOnlyDictionary<NonTerminal, IReadOnlyList<Rule>> rulesByProduced,
            IReadOnlyList<AmbiguityResolution> ambiguityResolutions,
            ILookup<NonTerminal, DiscriminatorContext> discriminatorContexts,
            IFirstFollowProvider firstFollowProvider)
        {
            this._rulesByProduced = rulesByProduced;
            this._ambiguityResolutions = ambiguityResolutions;
            this._discriminatorContexts = discriminatorContexts;
            this._firstFollowProvider = firstFollowProvider;

            // since the only rules that get added along the way are for discriminators, we can safely
            // build this cache only once (also this is OK since we only resolve ambiguities at the end)
            this._nonDiscriminatorSymbolReferences = new Lazy<ILookup<Symbol, (Rule rule, int index)>>(
                () => this._rulesByProduced.Where(kvp => !(kvp.Key.SyntheticInfo is DiscriminatorSymbolInfo))
                    .SelectMany(kvp => kvp.Value)
                    .SelectMany(r => r.Symbols.Select((s, i) => (referenced: s, index: i, rule: r)))
                    .ToLookup(t => t.referenced, t => (rule: t.rule, index: t.index))
            );
        }

        public (RuleRemainder Rule, string Error) ResolveAmbiguity(IReadOnlyList<RuleRemainder> rules, Token lookaheadToken)
        {
            // first try to resolve using specified resolutions
            var resolutionPreferredRules = this._ambiguityResolutions.Select(r => r.GetPreferredRuleOrDefault(rules, lookaheadToken))
                .Where(r => r != null)
                .ToArray();
            if (resolutionPreferredRules.Length > 0)
            {
                return (resolutionPreferredRules[0], resolutionPreferredRules.Length > 1 ? "multiple ambiguity resolutions apply" : null);
            }

            var contextualizer = new AmbiguityContextualizer(this._rulesByProduced, this._firstFollowProvider, this._discriminatorContexts);
            contextualizer.GetExpandedAmbiguityContexts(rules, lookaheadToken);
            return (rules[0], "see console output");

            // construct a helpful error message
            var longestConstruction = rules.Select(r => this.GetConstruction(r, lookaheadToken))
                .MaxBy(c => c.Count);
            var errorBuilder = new StringBuilder("Unable to choose between ");
            for (var i = 0; i < rules.Count; ++i)
            {
                if (i > 0) { errorBuilder.Append(", "); }
                errorBuilder.Append($"'{rules[i].Rule}' at index {rules[i].Start}");
            }
            errorBuilder.Append($" when encountering '{lookaheadToken}'. Full context: '{string.Join(" ", longestConstruction)}'");
            return (rules[0], errorBuilder.ToString());
        }

        private IReadOnlyList<Symbol> GetConstruction(RuleRemainder rule, Token lookaheadToken)
        {
            if (rule.Produced.SyntheticInfo is DiscriminatorSymbolInfo)
            {
                throw new NotImplementedException();
            }

            return this._firstFollowProvider.FirstOf(rule.Symbols).Contains(lookaheadToken)
                ? (IReadOnlyList<Symbol>)rule.Rule.Symbols.Take(rule.Start)
                    .Concat(this.GetSuffixConstruction(rule.Symbols.ToImmutableList(), lookaheadToken))
                    .ToArray()
                : this.GetOuterConstruction(rule.Produced, lookaheadToken, rule.Rule.Symbols.ToImmutableList(), ImmutableHashSet.Create(rule.Produced));
        }

        private ImmutableList<Symbol> GetOuterConstruction(
            NonTerminal produced,
            Token lookaheadToken,
            ImmutableList<Symbol> innerSymbols,
            ImmutableHashSet<NonTerminal> visited)
        {
            var outerReferences = this._nonDiscriminatorSymbolReferences.Value[produced]
                .Where(
                    r => this._firstFollowProvider.FirstOf(r.rule.Skip(r.index + 1).Symbols).Contains(lookaheadToken)
                        || (
                            !visited.Contains(r.rule.Produced)
                            && this._firstFollowProvider.NextOf(r.rule.Skip(r.index + 1)).Contains(lookaheadToken)
                        )
                )
                .ToArray();

            var outerSymbols = innerSymbols.ToBuilder();

            var prefixCommonPrefixLength = GetCommonPrefixLength(outerReferences.Select(r => r.rule.Symbols.Take(r.index).ToArray()).ToArray());
            if (outerReferences.All(r => r.index == prefixCommonPrefixLength))
            {
                outerSymbols.InsertRange(0, outerReferences[0].rule.Symbols.Take(prefixCommonPrefixLength));
            }

            var suffixCommonPrefixLength = GetCommonPrefixLength(outerReferences.Select(r => r.rule.Skip(r.index + 1).Symbols).ToArray());
            if (suffixCommonPrefixLength > 0)
            {
                outerSymbols.AddRange(outerReferences[0].rule.Skip(outerReferences[0].index + 1).Symbols.Take(suffixCommonPrefixLength));
            }

            if (outerReferences.All(r => r.rule.Symbols.Count == r.index + 1 + suffixCommonPrefixLength)
                && outerReferences.Select(r => r.rule.Produced).Distinct().Count() == 1
                && !this._firstFollowProvider.FirstOf(outerReferences[0].rule.Skip(outerReferences[0].index + 1).Symbols).Contains(lookaheadToken))
            {
                return this.GetOuterConstruction(outerReferences[0].rule.Produced, lookaheadToken, outerSymbols.ToImmutable(), visited.Add(outerReferences[0].rule.Produced));
            }

            return outerSymbols.ToImmutable();
        }

        private ImmutableList<Symbol> GetSuffixConstruction(ImmutableList<Symbol> suffix, Token lookaheadToken)
        {
            for (var i = 0; i < suffix.Count; ++i)
            {
                if (!(suffix[i] is NonTerminal nonTerminal))
                {
                    break;
                }

                if (this._firstFollowProvider.FirstOf(nonTerminal).Contains(lookaheadToken))
                {
                    if (this._firstFollowProvider.IsNullable(nonTerminal)
                        && this._firstFollowProvider.FollowOf(nonTerminal).Contains(lookaheadToken))
                    {
                        break;
                    }

                    var containingRuleSymbols = this._rulesByProduced[nonTerminal]
                        .Where(r => this._firstFollowProvider.FirstOf(r.Symbols).Contains(lookaheadToken))
                        .Select(r => r.Symbols)
                        .ToArray();
                    var commonPrefixLength = GetCommonPrefixLength(containingRuleSymbols);
                    if (commonPrefixLength == 0)
                    {
                        break;
                    }

                    var expandedSuffix = suffix.RemoveAt(i).InsertRange(i, containingRuleSymbols[0].Take(commonPrefixLength));
                    return this.GetSuffixConstruction(expandedSuffix, lookaheadToken);
                }
            }

            return suffix;
        }

        private static int GetCommonPrefixLength(IReadOnlyList<IReadOnlyList<Symbol>> symbolSequences)
        {
            var commonPrefixLength = Enumerable.Range(1, symbolSequences[0].Count)
                .LastOrDefault(count => symbolSequences.Skip(1).All(s => s.Take(count).SequenceEqual(symbolSequences[0].Take(count))));
            return commonPrefixLength;
        }
    }
}
