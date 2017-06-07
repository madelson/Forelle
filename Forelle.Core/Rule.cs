﻿using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;

namespace Forelle
{
    public sealed class Rule
    {
        public Rule(NonTerminal produced, params Symbol[] symbols)
            : this(produced, symbols?.AsEnumerable())
        {
        }

        public Rule(NonTerminal produced, IEnumerable<Symbol> symbols, ExtendedRuleInfo extendedInfo = null)
        {
            this.Produced = produced ?? throw new ArgumentNullException(nameof(produced));
            this.Symbols = new ReadOnlyCollection<Symbol>(Guard.NotNullOrContainsNullAndDefensiveCopy(symbols, nameof(symbols)));
            this.ExtendedInfo = extendedInfo ?? ExtendedRuleInfo.Empty;
        }

        public NonTerminal Produced { get; }
        public ReadOnlyCollection<Symbol> Symbols { get; }
        public ExtendedRuleInfo ExtendedInfo { get; }

        public override string ToString() => $"{this.Produced} -> {string.Join(" ", this.Symbols)}{this.GetExtendedRuleInfoString()}";

        internal string GetExtendedRuleInfoString()
        {
            var extendedRuleInfoString = this.ExtendedInfo.ToString();
            return extendedRuleInfoString.Length > 0 ? " " + extendedRuleInfoString : string.Empty;
        }
    }

    public sealed class ExtendedRuleInfo
    {
        internal static ExtendedRuleInfo Empty { get; } = new ExtendedRuleInfo(
            // these must be specified to avoid the constructor falling back to the Empty property
            parserStateRequirements: Enumerable.Empty<ParserStateVariableRequirement>(),
            parserStateActions: Enumerable.Empty<ParserStateVariableAction>()
        );

        public ExtendedRuleInfo(
            bool isRightAssociative = false,
            IEnumerable<ParserStateVariableRequirement> parserStateRequirements = null,
            IEnumerable<ParserStateVariableAction> parserStateActions = null,
            IEnumerable<Rule> mappedRules = null)
        {
            this.IsRightAssociative = IsRightAssociative;

            this.ParserStateRequirements = parserStateRequirements != null
                ? new ReadOnlyCollection<ParserStateVariableRequirement>(parserStateRequirements.Select(r => r ?? throw new ArgumentException("must not contain null", nameof(parserStateRequirements))).ToArray())
                : Empty.ParserStateRequirements;

            this.ParserStateActions = parserStateActions != null
                ? new ReadOnlyCollection<ParserStateVariableAction>(parserStateActions.Select(a => a ?? throw new ArgumentException("must not contain null", nameof(parserStateActions))).ToArray())
                : Empty.ParserStateActions;

            this.MappedRules = mappedRules != null
                ? new ReadOnlyCollection<Rule>(mappedRules.Select(r => r ?? throw new ArgumentException("must not contain null", nameof(mappedRules))).ToArray())
                : null;
        }

        /// <summary>
        /// Indicates whether the <see cref="Rule"/> should be treated as right-associative. If this flag
        /// is specified, the rule must be both right- and left- recursive
        /// </summary>
        public bool IsRightAssociative { get; }

        /// <summary>
        /// Indicates required settings for parser state variables at the time this <see cref="Rule"/> would be parsed. Absent
        /// those settings, the <see cref="Rule"/> cannot be parsed
        /// </summary>
        public ReadOnlyCollection<ParserStateVariableRequirement> ParserStateRequirements { get; }
        /// <summary>
        /// Indicates changes to parser state variables that occur after the <see cref="Rule"/> is parsed
        /// </summary>
        public ReadOnlyCollection<ParserStateVariableAction> ParserStateActions { get; }

        /// <summary>
        /// If non-null, this collection indicates that a parse of this <see cref="Rule"/> should instead be interpreted as a parse of
        /// 0 or more other <see cref="Rule"/>. This capability is used to "hide" certain rules (e. g. aliases) or to transform the grammar
        /// while still parsing the original user-specified rules (e. g. left-recursion elimination).
        /// 
        /// A null value of this property indicates that no mapping should be performed.
        /// </summary>
        public ReadOnlyCollection<Rule> MappedRules { get; }

        public override string ToString()
        {
            var parts = new List<string>();

            if (this.IsRightAssociative)
            {
                parts.Add("RIGHT ASSOCIATIVE");
            }

            parts.AddRange(this.ParserStateRequirements.Select(r => r.ToString()));
            parts.AddRange(this.ParserStateActions.Select(r => r.ToString()));

            if (this.MappedRules != null)
            {
                parts.Add($"PARSE AS {{{(this.MappedRules.Count == 0 ? " " : string.Join(", ", this.MappedRules))}}}");
            }

            return parts.Count > 0 ? $"{{ {string.Join(", ", parts)} }}" : string.Empty;
        }
    }

    public sealed class ParserStateVariableRequirement
    {
        public ParserStateVariableRequirement(string variableName, bool requiredValue = true)
        {
            if (string.IsNullOrEmpty(variableName)) { throw new FormatException($"{nameof(variableName)}: must not be null or empty"); }

            this.VariableName = variableName;
            this.RequiredValue = requiredValue;
        }

        public string VariableName { get; }
        public bool RequiredValue { get; }

        public override string ToString() => $"REQUIRE {(this.RequiredValue ? string.Empty : "!")}'{this.VariableName}'";
    }

    public sealed class ParserStateVariableAction
    {
        public ParserStateVariableAction(string variableName, ParserStateVariableActionKind kind)
        {
            if (string.IsNullOrEmpty(variableName)) { throw new FormatException($"{nameof(variableName)}: must not be null or empty"); }
            if (!Enum.IsDefined(typeof(ParserStateVariableActionKind), kind)) { throw new ArgumentException("undefined enum value", nameof(kind)); }

            this.VariableName = variableName;
            this.Kind = kind;
        }

        public string VariableName { get; }
        public ParserStateVariableActionKind Kind { get; }

        public override string ToString() => $"{this.Kind.ToString().ToUpperInvariant()} '{this.VariableName}'";
    }

    public enum ParserStateVariableActionKind
    {
        Push = 0,
        Set = 1,
        Pop = 2,
    }

    internal sealed class RuleRemainder
    {
        private ReadOnlyCollection<Symbol> _cachedSymbols;

        public RuleRemainder(Rule rule, int start)
        {
            if (rule == null) { throw new ArgumentNullException(nameof(rule)); }
            if (start < 0 || start > rule.Symbols.Count) { throw new ArgumentOutOfRangeException(nameof(start), start, $"must be a valid index in {rule}"); }

            this.Rule = rule;
            this.Start = start;
        }

        public Rule Rule { get; }
        public int Start { get; }
        public NonTerminal Produced => this.Rule.Produced;
        
        public ReadOnlyCollection<Symbol> Symbols
        {
            get
            {
                if (this._cachedSymbols == null)
                {
                    this._cachedSymbols = this.Start == 0
                        ? this.Rule.Symbols
                        : new ReadOnlyCollection<Symbol>(this.Rule.Symbols.Skip(this.Start).ToArray());
                }
                return this._cachedSymbols;
            }
        }

        public override bool Equals(object obj) => obj is RuleRemainder rule && Equals(this.Rule, rule.Rule) && this.Start == rule.Start;

        public override int GetHashCode() => (this.Rule, this.Start).GetHashCode();

        public override string ToString() =>
            $"{this.Produced} -> {(this.Start > 0 ? "... " : string.Empty)}{string.Join(" ", this.Symbols)}{this.Rule.GetExtendedRuleInfoString()}";
    }

    internal static class RuleHelpers
    {
        public static HashSet<Symbol> GetAllSymbols(this IEnumerable<Rule> rules)
        {
            return new HashSet<Symbol>(rules.Select(r => r.Produced).Concat(rules.SelectMany(r => r.Symbols)));
        }
    }
}
