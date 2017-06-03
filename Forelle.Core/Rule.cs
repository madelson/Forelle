using System;
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

        public Rule(NonTerminal produced, IEnumerable<Symbol> symbols)
        {
            this.Produced = produced ?? throw new ArgumentNullException(nameof(produced));
            this.Symbols = new ReadOnlyCollection<Symbol>(Guard.NotNullOrContainsNull(symbols, nameof(symbols)));
        }

        public NonTerminal Produced { get; }
        public ReadOnlyCollection<Symbol> Symbols { get; }

        public override string ToString() => $"{this.Produced} -> {string.Join(" ", this.Symbols)}";
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
            $"{this.Produced} -> {(this.Start > 0 ? "... " : string.Empty)}{string.Join(" ", this.Symbols)}";
    }
}
