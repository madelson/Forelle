using Medallion.Collections;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;

namespace Forelle.Parsing
{
    public sealed class AmbiguityResolution
    {
        private AmbiguityResolutionData _data;

        private AmbiguityResolution(AmbiguityResolutionData data)
        {
            this._data = data;
        }

        public string ProducedSymbolName => this._data.ProducedSymbolName;
        public string TokenName => this._data.TokenName;
        public (IReadOnlyList<string> RuleSymbolNames, int Index) PreferredRule => this._data.PreferredRule;
        public IReadOnlyList<(IReadOnlyList<string> RuleSymbolNames, int Index)> NonPreferredRules => this._data.NonPreferredRules;

        public static WhenParsingBuilder WhenParsing(string producedSymbolName)
        {
            return new WhenParsingBuilder(new AmbiguityResolutionData { ProducedSymbolName = ValidateSymbolName(producedSymbolName, nameof(producedSymbolName)) });
        }

        internal RuleRemainder GetPreferredRuleOrDefault(IReadOnlyList<RuleRemainder> rules, Token lookaheadToken)
        {
            if (this.TokenName != lookaheadToken.Name) { return null; }
            if (rules.Only(r => r.Produced).Name != this.ProducedSymbolName) { return null; }
            
            bool Matches(RuleRemainder rule, (IReadOnlyList<string> RuleSymbolNames, int Index) ruleSpec)
            {
                return rule.Start == ruleSpec.Index
                    && rule.Rule.Symbols.Select(s => s.Name).SequenceEqual(ruleSpec.RuleSymbolNames);
            }

            var preferredRule = rules.FirstOrDefault(r => Matches(r, this.PreferredRule));
            if (preferredRule == null) { return null; }
            if (!rules.All(r => r == preferredRule || this.NonPreferredRules.Any(npr => Matches(r, npr)))) { return null; }

            return preferredRule;
        }

        private static string ValidateSymbolName(string name, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new FormatException($"{parameterName} is not a valid symbol name");
            }
            return name;
        }

        private static (IReadOnlyList<string> RuleSymbolNames, int Index) ValidateRule(IEnumerable<string> ruleSymbolNames, int atIndex)
        {
            var ruleSymbolNamesList = (ruleSymbolNames ?? throw new ArgumentNullException(nameof(ruleSymbolNames)))
                .Select((name, index) => ValidateSymbolName(name, $"{nameof(ruleSymbolNames)}[{index}]"))
                .ToArray();

            if (atIndex < 0 || atIndex > ruleSymbolNamesList.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(atIndex), atIndex, $"must refer to a position in {nameof(ruleSymbolNames)}");
            }

            return (RuleSymbolNames: ruleSymbolNamesList, Index: atIndex);
        }

        internal struct AmbiguityResolutionData
        {
            internal string ProducedSymbolName;
            internal string TokenName;
            internal (IReadOnlyList<string> RuleSymbolNames, int Index) PreferredRule;
            internal ImmutableList<(IReadOnlyList<string> RuleSymbolNames, int Index)> NonPreferredRules;
        }

        public sealed class WhenParsingBuilder
        {
            private AmbiguityResolutionData _data;

            internal WhenParsingBuilder(AmbiguityResolutionData data) { this._data = data; }

            public UponEncounteringBuilder UponEncountering(string tokenName)
            {
                var dataCopy = this._data;
                dataCopy.TokenName = ValidateSymbolName(tokenName, nameof(tokenName));
                return new UponEncounteringBuilder(dataCopy);
            }
        }

        public sealed class UponEncounteringBuilder
        {
            private AmbiguityResolutionData _data;

            internal UponEncounteringBuilder(AmbiguityResolutionData data) { this._data = data; }

            public PreferBuilder Prefer(IEnumerable<string> ruleSymbolNames, int atIndex = 0)
            {
                var dataCopy = this._data;
                dataCopy.PreferredRule = ValidateRule(ruleSymbolNames, atIndex);
                return new PreferBuilder(dataCopy);
            }
        }

        public sealed class PreferBuilder
        {
            private AmbiguityResolutionData _data;

            internal PreferBuilder(AmbiguityResolutionData data) { this._data = data; }

            public OverBuilder Over(IEnumerable<string> ruleSymbolNames, int atIndex = 0)
            {
                return new OverBuilder(this._data).Over(ruleSymbolNames, atIndex);
            }
        }

        public sealed class OverBuilder
        {
            private AmbiguityResolutionData _data;

            internal OverBuilder(AmbiguityResolutionData data)
            {
                this._data = data;
                this._data.NonPreferredRules = this._data.NonPreferredRules
                    ?? ImmutableList<(IReadOnlyList<string> RuleSymbolNames, int Index)>.Empty;
            }

            public OverBuilder Over(IEnumerable<string> ruleSymbolNames, int atIndex = 0)
            {
                var newRule = ValidateRule(ruleSymbolNames, atIndex);

                if (new[] { this._data.PreferredRule }.Concat(this._data.NonPreferredRules)
                        .Any(r => r.RuleSymbolNames.SequenceEqual(newRule.RuleSymbolNames) && r.Index == newRule.Index))
                {
                    throw new ArgumentException("the provided rule has already been specified as a preferred or non-preferred rule");
                }

                var dataCopy = this._data;
                dataCopy.NonPreferredRules = dataCopy.NonPreferredRules.Add(newRule);
                return new OverBuilder(dataCopy);
            }

            public AmbiguityResolution ToAmbiguityResolution() => new AmbiguityResolution(this._data);
        }
    }
}
