//using Medallion.Collections;
//using System;
//using System.Collections.Generic;
//using System.Collections.Immutable;
//using System.Linq;
//using System.Text;

//namespace Forelle.Parsing.Construction
//{
//    internal class AmbiguityHelper
//    {
//        private readonly IReadOnlyDictionary<NonTerminal, IReadOnlyList<Rule>> _rules;
//        private readonly IFirstFollowProvider _firstFollowProvider;
//        private readonly IReadOnlyDictionary<NonTerminal, ImmutableList<DiscriminatorContext>> _discriminatorContexts;

//        /// <summary>
//        /// Maps all <see cref="Symbol"/>s to the set of non-discriminator rules where they are referenced and
//        /// the index positions of those references in the rule
//        /// </summary>
//        private readonly Lazy<ILookup<Symbol, (Rule rule, int index)>> _nonDiscriminatorSymbolReferences;

//        public AmbiguityHelper(
//            IReadOnlyDictionary<NonTerminal, IReadOnlyList<Rule>> rules,
//            IFirstFollowProvider firstFollowProvider,
//            IReadOnlyDictionary<NonTerminal, ImmutableList<DiscriminatorContext>> discriminatorContexts)
//        {
//            // no defensive copies here: we are ok with the state changing
//            this._rules = rules;
//            this._firstFollowProvider = firstFollowProvider;
//            this._discriminatorContexts = discriminatorContexts;

//            // since the only rules that get added along the way are for discriminators, we can safely
//            // build this cache only once
//            this._nonDiscriminatorSymbolReferences = new Lazy<ILookup<Symbol, (Rule rule, int index)>>(
//                () => this._rules.Where(kvp => !(kvp.Key.SyntheticInfo is DiscriminatorSymbolInfo))
//                    .SelectMany(kvp => kvp.Value)
//                    .SelectMany(r => r.Symbols.Select((s, i) => (referenced: s, index: i, rule: r)))
//                    .ToLookup(t => t.referenced, t => (rule: t.rule, index: t.index))
//            );
//        }

//        public List<AmbiguousSequence> ExpandContexts(IReadOnlyList<RuleRemainder> rules, Token lookaheadToken)
//        {
//            if (rules == null) { throw new ArgumentNullException(nameof(rules)); }
//            if (rules.Count == 0) { throw new ArgumentException("there must be at least one rule", nameof(rules)); }
//            if (lookaheadToken == null) { throw new ArgumentNullException(nameof(lookaheadToken)); }

//            // the idea is that this should take something like 
//            // Exp -> term . , Exp -> term . - Exp with a token "-" and realize that Exp is only followed by "-" in the case
//            // where term is cast so we have ( id ) -. Then we also realize that - is always followed by Exp giving us ( id ) - Exp
//            // as the full ambiguous clause

//            return this.ExpandContexts(rules, lookaheadToken, innerSequence: null).ToList();
//        }

//        private IEnumerable<AmbiguousSequence> ExpandContexts(IReadOnlyList<RuleRemainder> rules, Token lookaheadToken, AmbiguousSequence innerSequence)
//        {
//            // expand the prefix if possible
//            if (rules.Any(r => r.Start > 0))
//            {
//                if (rules.Select(r => r.Rule.Symbols.Take(r.Start))
//                        .Distinct(EqualityComparers.GetSequenceComparer<Symbol>())
//                        .Count() != 1)
//                {
//                    throw new InvalidOperationException("sanity check");
//                }

//                var prefix = rules[0].Rule.Symbols.Take(rules[0].Start).ToArray();
//                // retain the lookahead token iff it must appear AFTER the end of the rules. Otherwise we'll just tack
//                // it on as a suffix
//                var propagateLookaheadToken = lookaheadToken == null
//                    || (lookaheadToken != null && !rules.Any(r => this._firstFollowProvider.FirstOf(r.Symbols).Contains(lookaheadToken)));
//                return this.ExpandContexts(
//                    rules.Select(r => r.Rule.Skip(0)).ToArray(),
//                    propagateLookaheadToken ? lookaheadToken : null,
//                    new AmbiguousSequence(
//                        prefixSymbols: this.SpecializePrefix(prefix, lookaheadToken),
//                        innerSequence: innerSequence,
//                        suffixSymbols: propagateLookaheadToken 
//                            ? Enumerable.Empty<Symbol>() 
//                            : new[] { lookaheadToken }
//                    )
//                );
//            }
            
//            // expand discriminators
//            var produced = rules.Only(r => r.Produced);
//            if (produced.SyntheticInfo is DiscriminatorSymbolInfo)
//            {
//                return this._discriminatorContexts[produced]
//                    // only include compatible contexts
//                    .Where(c => lookaheadToken == null || c.LookaheadToken == lookaheadToken)
//                    .SelectMany(c => c.RuleToDiscriminatorRuleMapping.Where(
//                        kvp => rules.Any(r => r.Rule == kvp.Value)),
//                        (c, kvp) => (context: c, discriminatorRule: kvp.Value, mappedRule: kvp.Key)
//                    )
//                    .SelectMany(t => this.ExpandContexts(t.discriminatorRule, t.mappedRule, lookaheadToken, t.context, innerSequence));
//            }

//            // expand by wrapping with outer rules if possible
//            if (lookaheadToken != null
//                && !rules.Any(r => this._firstFollowProvider.FirstOf(r.Symbols).Contains(lookaheadToken)))
//            {
//                var outerReferences = this._nonDiscriminatorSymbolReferences.Value[produced]
//                    // if we have a lookahead token, filter out references that aren't compatible with it
//                    .Where(t => this._firstFollowProvider.NextOf(t.rule.Skip(t.index + 1)).Contains(lookaheadToken))
//                    .ToList();
//                if (outerReferences.Count == 1)
//                {
//                    var outerReference = outerReferences[0];
//                    var propagateLookaheadToken = !this._firstFollowProvider.FirstOf(outerReference.rule.Skip(outerReference.index + 1).Symbols)
//                        .Contains(lookaheadToken);
//                    return this.ExpandContexts(
//                        new[] { outerReference.rule.Skip(0) },
//                        propagateLookaheadToken ? lookaheadToken : null,
//                        new AmbiguousSequence(
//                            prefixSymbols: this.SpecializePrefix(outerReference.rule.Symbols.Take(outerReference.index).ToArray(), lookaheadToken),
//                            innerSequence: innerSequence,
//                            suffixSymbols: outerReference.rule.Symbols.Skip(outerReference.index + 1)
//                        )
//                    );
//                }
//            }

//            return lookaheadToken != null
//                ? new[] { new AmbiguousSequence(Enumerable.Empty<Symbol>(), innerSequence, new[] { lookaheadToken }) }
//                : new[] { innerSequence };
//        }

//        private IEnumerable<AmbiguousSequence> ExpandContexts(
//            Rule discriminatorRule,
//            RuleRemainder mappedRule,
//            Token lookaheadToken, 
//            DiscriminatorContext context, 
//            AmbiguousSequence innerSequence)
//        {
//            if (context.IsPrefix)
//            {
//                return this.ExpandContexts(
//                    new[] { mappedRule },
//                    lookaheadToken,
//                    innerSequence
//                );
//            }

//            var propagateLookaheadToken = lookaheadToken == null 
//                || !this._firstFollowProvider.FirstOf(discriminatorRule.Symbols).Contains(lookaheadToken);
//            return this.ExpandContexts(
//                new[] { mappedRule },
//                propagateLookaheadToken ? lookaheadToken : null,
//                new AmbiguousSequence(
//                    prefixSymbols: new[] { context.LookaheadToken },
//                    innerSequence: innerSequence,
//                    suffixSymbols: propagateLookaheadToken ? Enumerable.Empty<Symbol>() : new[] { lookaheadToken }
//                )
//            );
//        }

//        private IReadOnlyList<Symbol> SpecializePrefix(IReadOnlyList<Symbol> prefix, Token lookaheadToken)
//        {
//            if (lookaheadToken == null || prefix.Count == 0) { return prefix; }

//            var specialized = prefix.ToImmutableList();
//            for (var i = specialized.Count - 1; i >= 0; --i)
//            {
//                var symbol = specialized[i];
//                if (!(symbol is NonTerminal nonTerminal))
//                {
//                    break;
//                }

//                var rules = this._rules[nonTerminal];
//                if (rules.Count == 1)
//                {
//                    return this.SpecializePrefix(specialized.RemoveAt(i).InsertRange(i, rules[0].Symbols), lookaheadToken);
//                }

//                if (!this._firstFollowProvider.IsNullable(nonTerminal)
//                    || this._firstFollowProvider.FirstOf(nonTerminal).Contains(lookaheadToken))
//                {
//                    break;
//                }
//            }

//            return specialized;
//        }

//        public class AmbiguousSequence
//        {
//            public IReadOnlyList<Symbol> PrefixSymbols { get; }
//            public AmbiguousSequence InnerSequence { get; }
//            public IReadOnlyList<Symbol> SuffixSymbols { get; }

//            public AmbiguousSequence(IEnumerable<Symbol> prefixSymbols, AmbiguousSequence innerSequence, IEnumerable<Symbol> suffixSymbols)
//            {
//                this.PrefixSymbols = prefixSymbols.ToArray();
//                this.InnerSequence = innerSequence;
//                this.SuffixSymbols = suffixSymbols.ToArray();
//            }

//            public List<Symbol> ToSymbolList()
//            {
//                var result = new List<Symbol>();
//                this.ToSymbolList(result);
//                return result;
//            }

//            private void ToSymbolList(List<Symbol> list)
//            {
//                list.AddRange(this.PrefixSymbols);
//                this.InnerSequence?.ToSymbolList(list);
//                list.AddRange(this.SuffixSymbols);
//            }
//        }

//        //private IEnumerable<AmbiguityContext> ExpandContexts(RuleRemainder rule, Token lookaheadToken)
//        //{
//        //    if (rule.Produced.SyntheticInfo is DiscriminatorSymbolInfo)
//        //    {
//        //        var expanded = this._discriminatorContexts[rule.Produced]
//        //            .SelectMany(c => this.ExpandDiscriminatorContext(rule, lookaheadToken, c))
//        //            .ToList();
//        //        if (expanded.Count == 0) { throw new InvalidOperationException("sanity check: should always be able to expand discriminator"); }
//        //        return expanded;

//        //        //return contextRules.SelectMany(r => this.ExpandContexts(r, )

//        //        //foreach (var context in this._discriminatorContexts[rule.Produced])
//        //        //{
//        //        //    foreach (var kvp in context.RuleToDiscriminatorRuleMapping)
//        //        //    {
//        //        //        if (kvp.Value == rule
//        //        //            && context.IsPrefix
//        //        //                ? this._firstFollowProvider.NextOf(new RuleRemainder(kvp.Key.Rule, start: kvp.Key.Start + rule.Symbols.Count)).Contains(lookaheadToken)
//        //        //                : this._firstFollowProvider.FollowOf(kvp.Key.Rule).Contains(lookaheadToken))
//        //        //        {

//        //        //        }
//        //        //    }
//        //        //}
//        //    }

//        //    var outerReferences = this._nonDiscriminatorSymbolReferences.Value[rule.Produced]
//        //        // if we have a lookahead token, filter out references that aren't compatible with it
//        //        .Where(t => lookaheadToken == null
//        //            || this._firstFollowProvider.FirstOf(rule.Produced).Contains(lookaheadToken)
//        //            || this._firstFollowProvider.NextOf(t.rule.Skip(t.index + 1)).Contains(lookaheadToken))
//        //        .ToList();
            
//        //    if (outerReferences.Count == 1)
//        //    {
//        //        // if there is only one reference, try recursing
//        //        var reference = outerReferences[0];
//        //        // we pass on the lookahead token only if it can't appear in the remainder of the list
//        //        var propagateLookaheadToken = lookaheadToken != null
//        //            && !this._firstFollowProvider.FirstOf(reference.rule.Symbols.Skip(reference.index + 1)).Contains(lookaheadToken);

//        //        var recursiveExpansion = this.ExpandContexts(reference.rule.Skip(0), propagateLookaheadToken ? lookaheadToken : null);
//        //        return recursiveExpansion.Select(c => new AmbiguityContext(rule, lookaheadToken, outerContext: c, outerContextIndex: reference.index));
//        //    }

//        //    return new[] { new AmbiguityContext(rule, lookaheadToken) };
//        //}

//        //private IReadOnlyCollection<AmbiguityContext> ExpandDiscriminatorContext(Rule rule, Token lookheadToken, DiscriminatorContext context)
//        //{
//        //    // if the context isn't lookahead-compatible, we have no results
//        //    if (lookheadToken != null && lookheadToken != context.LookaheadToken)
//        //    {
//        //        return ImmutableList<AmbiguityContext>.Empty;
//        //    }

//        //    var mappedRules = context.RuleToDiscriminatorRuleMapping
//        //        .Where(kvp => kvp.Value == rule)
//        //        //.Where(kvp => !context.IsPrefix || this._firstFollowProvider.NextOf()
//        //        .Select(kvp => kvp.Key.Rule)
//        //        //.Where(r => !context.IsPrefix || )
//        //        .ToArray();
//        //    if (mappedRules.Length == 0) { throw new InvalidOperationException("sanity check"); }
            
//        //    // todo fix lookahead token
//        //    return mappedRules.SelectMany(r => this.ExpandContexts(r, context.IsPrefix ? null : lookheadToken))
//        //        .ToArray();
//        //}

//        //private IReadOnlyCollection<AmbiguityContext> ExpandDiscriminatorRule(
//        //    Rule discriminatorRule, 
//        //    RuleRemainder mappedRule, 
//        //    Token lookaheadToken, 
//        //    bool isPrefix)
//        //{

//        //}

//        //private class AmbiguityContext
//        //{
//        //    public RuleRemainder Rule { get; }
//        //    public Token LookaheadToken { get; }
//        //    public AmbiguityContext OuterContext { get; }
//        //    /// <summary>
//        //    /// The symbol index of <see cref="Rule"/> within <see cref="OuterContext"/>
//        //    /// </summary>
//        //    public int? OuterContextIndex { get; }

//        //    public AmbiguityContext(RuleRemainder rule, Token lookaheadToken = null, AmbiguityContext outerContext = null, int? outerContextIndex = null)
//        //    {
//        //        if ((outerContext == null) != outerContextIndex.HasValue)
//        //        {
//        //            throw new ArgumentException(nameof(outerContext) + " and " + nameof(outerContextIndex) + " must be specified together");
//        //        }

//        //        this.Rule = rule ?? throw new ArgumentNullException(nameof(rule));
//        //        this.LookaheadToken = lookaheadToken;
//        //        this.OuterContext = outerContext;
//        //        this.OuterContextIndex = outerContextIndex;
//        //    }

//        //    public (NonTerminal produced, ImmutableList<Symbol> symbols) ToFullSymbolList()
//        //    {
//        //        if (this.OuterContext == null)
//        //        {
//        //            return (produced: this.Rule.Produced, symbols: ImmutableList.CreateRange(this.Rule.Symbols.Concat(this.LookaheadToken != null ? new[] { this.LookaheadToken } : Enumerable.Empty<Symbol>())));
//        //        }

//        //        var outerSymbolList = this.OuterContext.ToFullSymbolList();
//        //        return (
//        //            produced: outerSymbolList.produced,
//        //            symbols: outerSymbolList.symbols.RemoveAt(this.OuterContextIndex.Value)
//        //                .InsertRange(this.OuterContextIndex.Value, this.Rule.Symbols)
//        //        );
//        //    }
//        //}
//    }
//}
