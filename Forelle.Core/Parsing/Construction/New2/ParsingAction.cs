using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;

namespace Forelle.Parsing.Construction.New2
{
    internal abstract class ParsingAction
    {
        private ImmutableHashSet<ParsingContext> _cachedResultDelegatedToContexts;

        public ImmutableHashSet<ParsingContext> ResultDelegatedToContexts
        {
            get
            {
                if (this.HasResult) { return ImmutableHashSet<ParsingContext>.Empty; }
                return (this._cachedResultDelegatedToContexts ?? (this._cachedResultDelegatedToContexts = this.GetResultDelegatedToContexts().ToImmutableHashSet()));
            }
        }

        public virtual bool HasResult => false;

        protected virtual IEnumerable<ParsingContext> GetResultDelegatedToContexts() => throw new NotImplementedException();
    }

    internal sealed class EatTokenAction : ParsingAction
    {
        public EatTokenAction(Token token, ParsingContext next)
        {
            this.Token = token ?? throw new ArgumentNullException(nameof(token));
            this.Next = next ?? throw new ArgumentNullException(nameof(next));
        }

        public Token Token { get; }
        public ParsingContext Next { get; }

        protected override IEnumerable<ParsingContext> GetResultDelegatedToContexts() => new[] { this.Next };
    }

    internal sealed class TokenSwitchAction : ParsingAction
    {
        public TokenSwitchAction(IEnumerable<KeyValuePair<Token, ParsingContext>> @switch)
        {
            this.Switch = @switch.ToDictionary(kvp => kvp.Key, kvp => kvp.Value ?? throw new ArgumentNullException(nameof(ParsingContext)));
        }

        public IReadOnlyDictionary<Token, ParsingContext> Switch { get; }

        protected override IEnumerable<ParsingContext> GetResultDelegatedToContexts() => this.Switch.Values;
    }

    internal sealed class ReduceAction : ParsingAction
    {
        public ReduceAction(IEnumerable<PotentialParseParentNode> parses)
        {
            this.Parses = parses.Select(p => p ?? throw new ArgumentNullException(nameof(PotentialParseParentNode)))
                .ToImmutableHashSet<PotentialParseParentNode>(PotentialParseNodeWithCursorComparer.Instance);
        }
        
        public ImmutableHashSet<PotentialParseParentNode> Parses { get; }
        public override bool HasResult => true;
    }

    internal sealed class ParseContextAction : ParsingAction
    {
        public ParseContextAction(ParsingContext context, ParsingContext next)
        {
            this.Context = context ?? throw new ArgumentNullException(nameof(context));
            this.Next = next ?? throw new ArgumentNullException(nameof(next));
        }

        public ParsingContext Context { get; }
        public ParsingContext Next { get; }

        protected override IEnumerable<ParsingContext> GetResultDelegatedToContexts() => new[] { this.Next };
    }
}
