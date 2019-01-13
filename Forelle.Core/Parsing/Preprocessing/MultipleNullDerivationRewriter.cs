using Medallion.Collections;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Forelle.Parsing.Preprocessing
{
    /// <summary>
    /// Detects ambiguities due to multiple null derivations for a symbol, and rewrites
    /// the grammar to resolve them
    /// </summary>
    internal class MultipleNullDerivationRewriter
    {
        private readonly List<string> _errors = new List<string>();
        private readonly IReadOnlyCollection<AmbiguityResolution> _ambiguityResolutions;
        private readonly Dictionary<NonTerminal, IReadOnlyList<Rule>> _rulesByProduced;
        private readonly IFirstFollowProvider _firstFollow;
        private readonly Dictionary<NonTerminal, (IReadOnlyList<Rule> nullableRules, NonTerminal nullSymbol, NonTerminal notNullSymbol)> _toRewrite = 
            new Dictionary<NonTerminal, (IReadOnlyList<Rule> nullableRules, NonTerminal nullSymbol, NonTerminal notNullSymbol)>();
        private readonly Lookup<IReadOnlyList<Symbol>, NonTerminal> _tupleSymbolsByElements = 
            new Lookup<IReadOnlyList<Symbol>, NonTerminal>(EqualityComparers.GetSequenceComparer<Symbol>());

        private MultipleNullDerivationRewriter(
            IReadOnlyList<Rule> rules,
            IReadOnlyCollection<AmbiguityResolution> ambiguityResolutions)
        {
            this._ambiguityResolutions = ambiguityResolutions;
            this._rulesByProduced = rules.GroupBy(g => g.Produced)
                .ToDictionary(g => g.Key, g => g.ToArray().As<IReadOnlyList<Rule>>());
            this._firstFollow = FirstFollowCalculator.Create(rules);

            // we don't expect to find any existing tuples since right now this is the
            // only file that creates them. However, since Tuple isn't strictly associated with
            // us this makes it more future-proof
            foreach (var symbol in this._rulesByProduced.Keys)
            {
                if (symbol.SyntheticInfo is TupleSymbolInfo tuple)
                {
                    this._tupleSymbolsByElements.Add(tuple.Elements, symbol);
                }
            }
        }
        
        public static (List<Rule> rewritten, List<string> errors) Rewrite(
            IReadOnlyList<Rule> rules, 
            IReadOnlyCollection<AmbiguityResolution> ambiguityResolutions)
        {
            // perform the rewrite
            var rewriter = new MultipleNullDerivationRewriter(rules, ambiguityResolutions);
            rewriter.Rewrite();

            // todo since we don't yet handle hidden or indirect left-recursion and our rewrite can introduce that, revalidate
            var rewrittenRules = rewriter._rulesByProduced.SelectMany(kvp => kvp.Value).ToList();
            List<Rule> resultRules;
            if (new RecursionValidator(rewrittenRules).GetErrors().Any())
            {
                rewriter._errors.Add("Unable to rewrite rules to remove multiple null derivations without introducing indirect or hidden left-recursion");
                resultRules = rules.ToList();
            }
            else
            {
                resultRules = rewrittenRules;
            }

            return (resultRules, rewriter._errors);
        }

        private void Rewrite()
        {
            // determine symbols to rewrite (those with multiple nullable rules)
            var nullableRulesByProduced = this._rulesByProduced.Values
                .Select(rules => rules.Where(this.IsNullable).ToArray())
                .Where(rules => rules.Length > 1)
                .ToDictionary(rules => rules[0].Produced, rules => rules);
            var symbolsToRewrite = nullableRulesByProduced.Keys.ToList();

            // determine the preferred null parse for each symbol
            var preferredNullParses = symbolsToRewrite.ToDictionary(
                s => s,
                s => this.GetPreferredParse(nullableRulesByProduced[s].Select(this.SimplestNullParseOf).ToArray())
            );

            // construct not-null synthetic symbols
            var notNullSymbols = symbolsToRewrite.Where(this.CanBeNonNull)
                .ToDictionary(s => s, s => NotNullSymbolInfo.CreateNotNullSymbol(s));

            // save off the original rules
            var rulesToBeReplaced = symbolsToRewrite.ToDictionary(
                s => s,
                s => this._rulesByProduced[s].ToArray()
            );

            // replace the original rules with references to the new symbols (so we now have A -> null | NotNull<A>).
            // The benefit of an explicit NotNull<T> symbol is that it handles the case where the not null derivations are recursive
            symbolsToRewrite.ForEach(s => {
                var newRules = new List<Rule> { CreateRule(produced: s, parseAs: preferredNullParses[s]) };
                if (notNullSymbols.TryGetValue(s, out var notNullSymbol))
                {
                    newRules.Add(new Rule(s, new[] { notNullSymbols[s] }, ExtendedRuleInfo.Unmapped));
                }
                this._rulesByProduced[s] = newRules;
            });

            // add not null symbol rules
            foreach (var (symbol, notNullSymbol) in notNullSymbols)
            {
                this._rulesByProduced.Add(notNullSymbol, this.CreateNotNullSymbolRules(notNullSymbol, rulesToBeReplaced[symbol]));
            }
        }
        
        /// <summary>
        /// Given a set of null derivations for a symbol, uses <see cref="_ambiguityResolutions"/> or order
        /// to pick the preferred one. If no <see cref="AmbiguityResolution"/> is available, an error is logged
        /// </summary>
        private PotentialParseParentNode GetPreferredParse(IReadOnlyList<PotentialParseParentNode> nullParses)
        {
            // since in some cases there may be infinite potential null parses for a given symbol,
            // we can't insist that all are provided in the ambiguity resolution. Instead, we allow
            // any resolution to apply where all rules are for the right symbol type and all parses are null
            var matchingResolution = this._ambiguityResolutions.FirstOrDefault(
                r => r.OrderedParses.All(n => n.Symbol == nullParses[0].Symbol && n.LeafCount == 0)
            );
            if (matchingResolution != null)
            {
                return (PotentialParseParentNode)matchingResolution.PreferredParse;
            }

            // construct an error
            var error = new StringBuilder($"Unable to decide between multiple parse trees for the empty sequence of symbols, when parsing {nullParses[0].Symbol}:")
                .AppendLine();
            foreach (var nullParse in nullParses)
            {
                error.AppendLine($"\t{nullParse}");
            }

            this._errors.Add(error.ToString());
            return nullParses[0];
        }

        /// <summary>
        /// Creates a rule for symbol <paramref name="produced"/> which, when parsed, will
        /// generate an AST like <paramref name="parseAs"/>
        /// </summary>
        private Rule CreateRule(NonTerminal produced, PotentialParseParentNode parseAs)
        {
            // rules that still need to be processed to "reduce" the shifted symbols
            var rulesToProcess = new List<Rule>();
            var shiftedSymbols = new Stack<Symbol>();
            EmulateParse(parseAs, isLastChild: true);

            return new Rule(
                produced,
                shiftedSymbols.Reverse(),
                parseAs.Rule.ExtendedInfo.Update(mappedRules: rulesToProcess)
            );
            
            void EmulateParse(PotentialParseNode node, bool isLastChild)
            {
                if (node is PotentialParseParentNode parent)
                {
                    for (var i = 0; i < parent.Children.Count; ++i)
                    {
                        EmulateParse(parent.Children[i], isLastChild: i == parent.Children.Count - 1);
                    }
                    rulesToProcess.Add(parent.Rule);
                    
                    if (!isLastChild)
                    {
                        // if we get here, it means that we are trying to parse as a something like A(B(x y) ...): we need
                        // to know to reduce by B -> x y before moving on to ... Therefore, we create a tuple symbol Tuple<x, y>
                        // which parses as B -> x y. We then pop x and y of the shifted stack and replace with Tuple<x, y>

                        // this math is a bit tricky. We need to figure out how many of the currently-shifted symbols will get
                        // popped in processing our rule set. For each rule starting with the first, we pop the symbols for that
                        // rule and then push the produced value of that rule. This is true up until the last rule in the sequence,
                        // which gets pushed as a result of parsing the entire tuple. Therefore, we sum the rule symbol counts
                        // and subtract off one for each intermediate pushed symbol
                        var tupleElements = new Symbol[rulesToProcess.Sum(r => r.Symbols.Count) - (rulesToProcess.Count - 1)];
                        for (var i = tupleElements.Length - 1; i >= 0; --i)
                        {
                            tupleElements[i] = shiftedSymbols.Pop();
                        }
                        var tupleRule = GetTupleRule(tupleElements, rulesToProcess);
                        rulesToProcess.Clear();
                        shiftedSymbols.Push(tupleRule.Produced);
                    }
                }
                else
                {
                    shiftedSymbols.Push(node.Symbol);
                }
            }

            // gets a rule which parses the given list of symbols and maps to the specified set of rules
            Rule GetTupleRule(IReadOnlyList<Symbol> elements, List<Rule> mappedRules)
            {
                var existingTupleSymbols = this._tupleSymbolsByElements[elements];
                var extendedInfo = mappedRules[mappedRules.Count - 1].ExtendedInfo.Update(mappedRules: mappedRules);
                var matchingRule = existingTupleSymbols.Select(t => this._rulesByProduced[t].Single())
                    .FirstOrDefault(r => Equals(r.ExtendedInfo, extendedInfo));
                if (matchingRule != null) { return matchingRule; }

                var newRule = TupleSymbolInfo.CreateRule(elements, existingEquivalentTupleCount: existingTupleSymbols.Count(), extendedInfo: extendedInfo);
                this._rulesByProduced.Add(newRule.Produced, new[] { newRule });
                this._tupleSymbolsByElements.Add(elements, newRule.Produced);
                return newRule;
            }
        }

        private bool CanBeNonNull(NonTerminal nonTerminal)
        {
            var checkedSymbols = new HashSet<Symbol>();
            return CanBeNonNullHelper(nonTerminal);

            bool CanBeNonNullHelper(Symbol symbol)
            {
                return !this.IsNullable(symbol)
                    // since we're doing one giant ||/Any, there's no point in checking any symbol more than once anywhere in the recursion tree
                    // and we can treat such rechecks as having a false result with no harm
                    || this._rulesByProduced[(NonTerminal)symbol].SelectMany(r => r.Symbols).Where(checkedSymbols.Add).Any(CanBeNonNullHelper);
            }
        }

        private Rule[] CreateNotNullSymbolRules(NonTerminal notNullSymbol, IReadOnlyList<Rule> originalRules)
        {
            var nonNullDerivations = originalRules.SelectMany(this.FindNonNullDerivations);
            return nonNullDerivations.Select(parseAs => CreateRule(notNullSymbol, parseAs))
                .ToArray();
        }

        /// <summary>
        /// Finds a minimal set of non-nullable parse trees for <paramref name="rule"/>.
        /// 
        /// Note that there are multiple such minimal sets. For example, let's say we have
        /// A -> B C | empty, B -> + | empty, C -> - | empty
        /// 
        /// Then for A -> B C we will produce:
        /// A(B(+) C)
        /// A(B() C(-))
        /// 
        /// However, we could equivalently produce:
        /// A(B(+) C())
        /// A(B C(-))
        /// 
        /// The reason there are multiple is that we are saving on rules by avoiding generating a full cross-product, which in this case would be:
        /// A(B(+) C())
        /// A(B(+) C(-))
        /// A(B() C(-))
        /// </summary>
        private IEnumerable<PotentialParseParentNode> FindNonNullDerivations(Rule rule)
        {
            var result = new List<PotentialParseParentNode>();
            GatherNonNullDerivations(ToNode(rule));
            return result;

            void GatherNonNullDerivations(PotentialParseParentNode toExpand)
            {
                if (toExpand.LeafCount == 0) { return; } // all nulled out

                var firstLeaf = toExpand.GetLeaves().First();
                if (!this.IsNullable(firstLeaf.Symbol))
                {
                    result.Add(toExpand); // found a non-nullable derivation
                    return;
                }

                foreach (var expansionRule in this._rulesByProduced[(NonTerminal)firstLeaf.Symbol])
                {
                    GatherNonNullDerivations((PotentialParseParentNode)ReplaceFirstLeaf(toExpand, ToNode(expansionRule)));
                }
            }
        }

        private bool IsNullable(Symbol symbol)
        {
            switch (symbol.SyntheticInfo)
            {
                // before checking firstFollow (which doesn't update as we add symbols), special-case
                // the synthetic types we create in this class
                case NotNullSymbolInfo _: return false;
                case TupleSymbolInfo tuple: return tuple.Elements.All(this.IsNullable);
                default: return this._firstFollow.IsNullable(symbol);
            }
        }

        private bool IsNullable(Rule rule) => rule.Symbols.All(this.IsNullable);

        /// <summary>
        /// Finds a null derivation of <paramref name="rule"/> with the fewest
        /// total <see cref="PotentialParseNode"/>s in the tree parse tree.
        /// </summary>
        private PotentialParseParentNode SimplestNullParseOf(Rule rule)
        {
            var priorityQueue = new PriorityQueue<PotentialParseParentNode>(
                Comparers.Create((PotentialParseParentNode n) => n.CountNodes())
            );
            priorityQueue.Enqueue(ToNode(rule));

            while (true)
            {
                var current = priorityQueue.Dequeue();

                // when we find a null derivation, stop. We know that this will terminate
                // because a nullable symbol MUST have a null derivation
                if (current.LeafCount == 0) { return current; }

                var firstLeaf = current.GetLeaves().First();
                foreach (var expansionRule in this._rulesByProduced[(NonTerminal)firstLeaf.Symbol]
                    .Where(this.IsNullable))
                {
                    priorityQueue.Enqueue((PotentialParseParentNode)ReplaceFirstLeaf(current, ToNode(expansionRule)));
                }
            }
        }

        private static PotentialParseNode ReplaceFirstLeaf(PotentialParseNode node, PotentialParseNode replacement)
        {
            if (node is PotentialParseParentNode parent)
            {
                var indexOfFirstChildWithLeaves = Enumerable.Range(0, parent.Children.Count)
                    .First(i => parent.Children[i].LeafCount > 0); // will throw if node is leafless (invalid arg)
                return new PotentialParseParentNode(
                    parent.Rule,
                    parent.Children.Select((ch, index) => index == indexOfFirstChildWithLeaves ? ReplaceFirstLeaf(ch, replacement) : ch)
                );
            }

            return replacement;
        }

        /// <summary>
        /// Creates a <see cref="PotentialParseParentNode"/> representing the "default" parse of <paramref name="rule"/>:
        /// Produced -> S1 S2 ...
        /// </summary>
        private static PotentialParseParentNode ToNode(Rule rule) =>
            new PotentialParseParentNode(rule, rule.Symbols.Select(s => new PotentialParseLeafNode(s)));
    }
}
