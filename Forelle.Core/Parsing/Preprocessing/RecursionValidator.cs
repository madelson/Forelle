using Medallion.Collections;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Forelle.Parsing.Preprocessing
{
    internal class RecursionValidator
    {
        private readonly IReadOnlyCollection<Rule> _rules;
        private readonly ILookup<NonTerminal, Rule> _rulesByProduced;
        private readonly IFirstFollowProvider _firstFollow;
        private readonly IReadOnlyDictionary<NonTerminal, NonTerminal> _aliases;

        public RecursionValidator(IReadOnlyList<Rule> rules)
        {
            this._rules = rules;
            this._rulesByProduced = rules.ToLookup(r => r.Produced);
            this._firstFollow = FirstFollowCalculator.Create(rules);
            this._aliases = AliasHelper.FindAliases(rules);
        }

        public IEnumerable<string> GetErrors()
        {
            return this.GetSymbolRecursionErrors()
                .Concat(this.GetRightAssociativityErrors())
                .Concat(this.GetLeftRecursionErrors())
                .Concat(this.GetAliasCycleErrors());
        }

        private IEnumerable<string> GetSymbolRecursionErrors()
        {
            var nonRecursive = new HashSet<NonTerminal>();

            bool changed;
            do
            {
                changed = false;

                foreach (var symbolRules in this._rulesByProduced)
                {
                    // a symbol is non-recursive if it has any rule whose symbols are all non-recursive. A symbol is non-recursive
                    // if it is a Token, an established non-recursive NonTerminal, or an undefined NonTerminal (to avoid confusing errors)
                    if (symbolRules.Any(r => r.Symbols.All(s => s is Token || (s is NonTerminal n && (nonRecursive.Contains(n) || !this._rulesByProduced[n].Any())))))
                    {
                        changed |= nonRecursive.Add(symbolRules.Key);
                    }
                }
            }
            while (changed);

            return this._rulesByProduced.Select(g => g.Key)
                .Where(s => !nonRecursive.Contains(s))
                .Select(s => $"All rules for symbol '{s}' recursively contain '{s}'");
        }

        private IEnumerable<string> GetRightAssociativityErrors()
        {
            bool isDirectlyRecursive(Rule rule, int index) => rule.Symbols[index] is NonTerminal nonTerminal
                && (
                    nonTerminal == rule.Produced
                    // we don't have to check the other way, because nonTerminal cannot both be an alias of produced
                    // and appear in a 3-symbol rule for produced
                    || AliasHelper.IsAliasOf(rule.Produced, nonTerminal, this._aliases)
                );

            return this._rules.Select(
                r => !r.ExtendedInfo.IsRightAssociative ? null
                    : r.Symbols.Count < 2 ? $"Rule {r} must have at least two symbols to be a right-associative binary rule"
                    : !isDirectlyRecursive(r, 0) ? $"The first symbol of rule {r} must be directly recursive in order to be a right-associative binary rule"
                    : !isDirectlyRecursive(r, r.Symbols.Count - 1) ? $"The last symbol of rule {r} must be directly recursive in order to be a right-associative binary rule"
                    : null
                )
                .Where(m => m != null);
        }

        private IEnumerable<string> GetLeftRecursionErrors()
        {
            string toString(RecursionProblem problem)
            {
                switch (problem)
                {
                    case RecursionProblem.Indirect: return "indirect";
                    case RecursionProblem.Hidden: return "hidden";
                    case RecursionProblem.Indirect | RecursionProblem.Hidden : return "indirect hidden";
                    default: throw new InvalidOperationException($"Unexpected value {problem}");
                }
            }

            return this._rules.Select(r =>
                    this.DetectProblematicLeftRecursion(ImmutableList.Create(r), ImmutableHashSet<NonTerminal>.Empty, RecursionProblem.None)
                )
                .Where(t => t.HasValue)
                .Select(t => $"Rule {t.Value.path[0]} exhibits {toString(t.Value.problem)} left-recursion{(t.Value.problem.HasFlag(RecursionProblem.Indirect) ? $" along path {string.Join(" => ", t.Value.path.Skip(1))}" : string.Empty)}");
        }

        private (ImmutableList<Rule> path, RecursionProblem problem)? DetectProblematicLeftRecursion(
            ImmutableList<Rule> context,
            ImmutableHashSet<NonTerminal> visited,
            RecursionProblem problem)
        {
            var rule = context[context.Count - 1];
            // avoid infinite recursion
            if (visited.Contains(rule.Produced)) { return null; }

            var produced = context[0].Produced;
            var newVisited = visited.Add(rule.Produced);
            var currentProblem = problem;
            for (var i = 0; i < rule.Symbols.Count; ++i)
            {
                var symbol = rule.Symbols[i] as NonTerminal;
                if (symbol == null) { break; }

                // found left recursion
                if (symbol == produced)
                {
                    if (currentProblem != RecursionProblem.None)
                    {
                        return (path: context, problem: currentProblem);
                    }

                    // otherwise, we don't need to recurse but we still need to check hidden
                }
                else
                {
                    // otherwise, recurse on each rule
                    var isAlias = AliasHelper.IsAliasOf(symbol, produced, this._aliases) 
                        || AliasHelper.IsAliasOf(produced, symbol, this._aliases);
                    foreach (var symbolRule in this._rulesByProduced[symbol])
                    {
                        var result = this.DetectProblematicLeftRecursion(
                            context.Add(symbolRule),
                            newVisited,
                            currentProblem | (isAlias ? RecursionProblem.None : RecursionProblem.Indirect)
                        );
                        if (result.HasValue) { return result; }
                    }
                }

                // continue past the symbol if it's nullable. Once we do this we're in the realm of hidden left recursion
                if (!this._firstFollow.IsNullable(symbol)) { break; }
                currentProblem |= RecursionProblem.Hidden;
            }

            // if we reach here without returning, we found nothing
            return null;
        }

        /// <summary>
        /// Finds errors where the alias relationship forms a cycle
        /// </summary>
        private IEnumerable<string> GetAliasCycleErrors()
        {
            return this._aliases.Keys
                // find alias => aliased => ... paths
                .Select(
                    s => Traverse.Along(s, a => this._aliases.TryGetValue(a, out var aliased) ? aliased : null)
                        // a traversed cycle is infinite. A non-cycle can have at most N + 1 elements where N is the number
                        // of aliases (+1 for the last alias in the chain which aliases a non-alias). Thus, once we see more 
                        // than N + 1 elements we know there has been a repeat. We take a full 2(N + 1) elements so that all 
                        // elements that are part of the cycle appear at least twice
                        .Take(2 * (this._aliases.Count + 1))
                        .ToArray()
                )
                // take only those with repeats (cycles)
                .Where(s => s.Length > this._aliases.Count + 1)
                // in each cycle, find the lowest element whose count appears twice. This will be used to identify the cycle
                // and eliminate duplicates. For example, we might find both X => A => B => A and Y => A => B => A
                .Select(s => (sequence: s, keySymbol: s.GroupBy(e => e).Where(g => g.Count() > 1).MinBy(g => g.Key.Name).Key))
                // group duplicate cycles together
                .GroupBy(t => t.keySymbol, t => t.sequence)
                // select out the actual cycle: the sequence from [key .. key)
                .Select(g => g.First().SkipWhile(s => s != g.Key).TakeWhile((s, index) => index == 0 || s != g.Key).ToList())
                .Select(c => $"Invalid recursive cycle found: {string.Join(" -> ", c)} -> {c[0]}");
        }

        [Flags]
        private enum RecursionProblem
        {
            None = 0,
            Hidden = 1,
            Indirect = 2,
        }
    }
}
