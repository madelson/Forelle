using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;

namespace Forelle.Parsing.Construction.New2
{
    internal abstract class ParsingAction
    {
        protected static readonly IEqualityComparer<PotentialParseParentNode> NodeComparer = PotentialParseNodeWithCursorComparer.Instance;

        private ImmutableHashSet<ParsingContext> _cachedSubContexts, _cachedNextContexts, _cachedReferencedContexts;
        private IReadOnlyDictionary<PotentialParseParentNode, ImmutableHashSet<PotentialParseParentNode>> _cachedNextToCurrentNodeMapping;

        protected ParsingAction(ParsingContext current)
        {
            this.Current = current ?? throw new ArgumentNullException(nameof(current));
        }

        public ParsingContext Current { get; }

        public ImmutableHashSet<ParsingContext> SubContexts
        {
            get
            {
                if (this._cachedSubContexts == null)
                {
                    this._cachedSubContexts = this.GetSubContexts().ToImmutableHashSet();
                    Invariant.Require(!this._cachedSubContexts.Contains(null));
                }
                return this._cachedSubContexts;
            }
        }

        public ImmutableHashSet<ParsingContext> NextContexts
        {
            get
            {
                if (this._cachedNextContexts == null)
                {
                    this._cachedNextContexts = this.GetNextContexts().ToImmutableHashSet();
                    Invariant.Require(!this._cachedNextContexts.Contains(null));
                }
                return this._cachedNextContexts;
            }
        }

        public ImmutableHashSet<ParsingContext> ReferencedContexts => this._cachedReferencedContexts ?? (this._cachedReferencedContexts = this.NextContexts.Union(this.SubContexts));

        public IReadOnlyDictionary<PotentialParseParentNode, ImmutableHashSet<PotentialParseParentNode>> NextToCurrentNodeMapping
        {
            get
            {
                if (this._cachedNextToCurrentNodeMapping == null)
                {
                    this._cachedNextToCurrentNodeMapping = this.GetNextToCurrentNodeMapping()
                        .GroupBy(t => t.next, t => t.current, NodeComparer)
                        .ToDictionary(g => g.Key, g => g.ToImmutableHashSet(NodeComparer), NodeComparer);
                    Invariant.Require(!this._cachedNextToCurrentNodeMapping.Values.Any(s => s.Contains(null)));
                    Invariant.Require(this.NextContexts.Select(c => c.Nodes).Aggregate((s1, s2) => s1.Union(s2)).SetEquals(this._cachedNextToCurrentNodeMapping.Keys));
                    Invariant.Require(this.Current.Nodes.IsSupersetOf(this._cachedNextToCurrentNodeMapping.SelectMany(kvp => kvp.Value)));
                }
                return this._cachedNextToCurrentNodeMapping;
            }
        }

        protected abstract IEnumerable<ParsingContext> GetSubContexts();
        protected abstract IEnumerable<ParsingContext> GetNextContexts();
        protected abstract IEnumerable<(PotentialParseParentNode next, PotentialParseParentNode current)> GetNextToCurrentNodeMapping();
    }

    internal sealed class EatTokenAction : ParsingAction
    {
        public EatTokenAction(ParsingContext current, Token token, ParsingContext next)
            : base(current)
        {
            this.Token = token ?? throw new ArgumentNullException(nameof(token));
            this.Next = next ?? throw new ArgumentNullException(nameof(next));
            Invariant.Require(current.LookaheadTokens.Count == 1 && current.LookaheadTokens.Single() == token);
        }

        public Token Token { get; }
        public ParsingContext Next { get; }

        protected override IEnumerable<ParsingContext> GetSubContexts() => ImmutableHashSet<ParsingContext>.Empty;
        protected override IEnumerable<ParsingContext> GetNextContexts() => ImmutableHashSet.Create(this.Next);
        protected override IEnumerable<(PotentialParseParentNode next, PotentialParseParentNode current)> GetNextToCurrentNodeMapping() =>
            this.Next.Nodes.GroupJoin(
                this.Current.Nodes, n => n, c => c,
                // we match by looking for where the next cursor has advanced by one
                (next, currents) => (next, currents.Single(c => c.GetCursorLeafIndex() + 1 == next.GetCursorLeafIndex())),
                PotentialParseNode.Comparer.As<IEqualityComparer<PotentialParseParentNode>>()
            );
    }

    internal sealed class TokenSwitchAction : ParsingAction
    {
        public TokenSwitchAction(ParsingContext current, IEnumerable<KeyValuePair<Token, ParsingContext>> @switch)
            : base(current)
        {
            this.Switch = @switch.ToDictionary(kvp => kvp.Key, kvp => kvp.Value ?? throw new ArgumentNullException(nameof(ParsingContext)));
            Invariant.Require(current.LookaheadTokens.SetEquals(this.Switch.Keys));
        }

        public IReadOnlyDictionary<Token, ParsingContext> Switch { get; }

        protected override IEnumerable<ParsingContext> GetSubContexts() => ImmutableHashSet<ParsingContext>.Empty;
        protected override IEnumerable<ParsingContext> GetNextContexts() => this.Switch.Values;
        protected override IEnumerable<(PotentialParseParentNode next, PotentialParseParentNode current)> GetNextToCurrentNodeMapping() =>
            this.NextContexts.SelectMany(c => c.Nodes)
                .Join(this.Current.Nodes, n => n, c => c, (next, current) => (next, current), NodeComparer);
    }

    internal sealed class ReduceAction : ParsingAction
    {
        public ReduceAction(ParsingContext current, IEnumerable<PotentialParseParentNode> parses)
            : base(current)
        {
            this.Parses = parses.ToImmutableHashSet(NodeComparer);
            Invariant.Require(!this.Parses.Contains(null));
        }
        
        public ImmutableHashSet<PotentialParseParentNode> Parses { get; }

        protected override IEnumerable<ParsingContext> GetSubContexts() => ImmutableHashSet<ParsingContext>.Empty;
        protected override IEnumerable<ParsingContext> GetNextContexts() => ImmutableHashSet<ParsingContext>.Empty;
        protected override IEnumerable<(PotentialParseParentNode next, PotentialParseParentNode current)> GetNextToCurrentNodeMapping() => 
            Enumerable.Empty<(PotentialParseParentNode next, PotentialParseParentNode current)>();
    }

    internal sealed class ParseSubContextAction : ParsingAction
    {
        public ParseSubContextAction(ParsingContext current, ParsingContext subContext, ParsingContext next)
            : base(current)
        {
            this.SubContext = subContext ?? throw new ArgumentNullException(nameof(subContext));
            this.Next = next ?? throw new ArgumentNullException(nameof(next));
        }

        public ParsingContext SubContext { get; }
        public ParsingContext Next { get; }

        protected override IEnumerable<ParsingContext> GetSubContexts() => ImmutableHashSet.Create(this.SubContext);
        protected override IEnumerable<ParsingContext> GetNextContexts() => ImmutableHashSet.Create(this.Next);
        protected override IEnumerable<(PotentialParseParentNode next, PotentialParseParentNode current)> GetNextToCurrentNodeMapping() =>
            this.Next.Nodes.GroupJoin(
                this.Current.Nodes, n => n, c => c,
                // we match by looking for where the next cursor has advanced by one
                (next, currents) => (next, currents.Single(c => c.GetCursorLeafIndex() + 1 == next.GetCursorLeafIndex())),
                PotentialParseNode.Comparer.As<IEqualityComparer<PotentialParseParentNode>>()
            );
    }
    
    internal sealed class DelegateToSpecializedContextAction : ParsingAction
    {
        private readonly IReadOnlyCollection<(PotentialParseParentNode next, PotentialParseParentNode current)> _nextToCurrentNodeMapping;

        public DelegateToSpecializedContextAction(
            ParsingContext current,
            ParsingContext next,
            IEnumerable<(PotentialParseParentNode next, PotentialParseParentNode current)> nextToCurrentNodeMapping)
            : base(current)
        {
            this.Next = next ?? throw new ArgumentNullException(nameof(next));
            this._nextToCurrentNodeMapping = nextToCurrentNodeMapping.ToArray();
            // TODO I don't think this can ever happen, since it would require 2 different nodes to specialize to the same node. I'm not convinced
            // that that can actually happen...
            Invariant.Require(this._nextToCurrentNodeMapping.Select(t => t.next).Distinct(NodeComparer).Count() == this._nextToCurrentNodeMapping.Count);
        }

        public ParsingContext Next { get; }

        protected override IEnumerable<ParsingContext> GetSubContexts() => ImmutableHashSet<ParsingContext>.Empty;
        protected override IEnumerable<ParsingContext> GetNextContexts() => ImmutableHashSet.Create(this.Next);
        protected override IEnumerable<(PotentialParseParentNode next, PotentialParseParentNode current)> GetNextToCurrentNodeMapping() => this._nextToCurrentNodeMapping;
    }

    internal abstract class SubContextSwitchActionBase : ParsingAction
    {
        private readonly IReadOnlyCollection<(PotentialParseParentNode next, PotentialParseParentNode current)> _nextToCurrentNodeMapping;

        protected SubContextSwitchActionBase(
            ParsingContext current, 
            ParsingContext subContext, 
            IEnumerable<(PotentialParseParentNode next, PotentialParseParentNode current)> nextToCurrentNodeMapping)
            : base(current)
        {
            this.SubContext = subContext ?? throw new ArgumentNullException(nameof(subContext));
            this._nextToCurrentNodeMapping = nextToCurrentNodeMapping.ToArray();
        }

        public ParsingContext SubContext { get; }

        protected sealed override IEnumerable<ParsingContext> GetSubContexts() => ImmutableHashSet.Create(this.SubContext);
        protected sealed override IEnumerable<(PotentialParseParentNode next, PotentialParseParentNode current)> GetNextToCurrentNodeMapping() => this._nextToCurrentNodeMapping;
    }

    internal sealed class UnresolvedSubContextSwitchAction : SubContextSwitchActionBase
    {
        private readonly ImmutableHashSet<ParsingContext> _nextContexts;
        
        public UnresolvedSubContextSwitchAction(
            ParsingContext current,
            ParsingContext subContext, 
            IEnumerable<ParsingContext> potentialNextContexts,
            IEnumerable<(PotentialParseParentNode subContext, PotentialParseParentNode next)> @switch,
            IEnumerable<(PotentialParseParentNode next, PotentialParseParentNode current)> nextToCurrentNodeMapping)
            : base(current, subContext, nextToCurrentNodeMapping)
        {
            this.Switch = @switch.ToArray();
            Invariant.Require(!this.Switch.Any(t => t.subContextNode == null || t.nextNode == null));

            this._nextContexts = potentialNextContexts.ToImmutableHashSet();
        }

        public IReadOnlyCollection<(PotentialParseParentNode subContextNode, PotentialParseParentNode nextNode)> Switch { get; }

        protected override IEnumerable<ParsingContext> GetNextContexts() => this._nextContexts;
    }

    internal sealed class SubContextSwitchAction : SubContextSwitchActionBase
    {
        public SubContextSwitchAction(
            ParsingContext current, 
            ParsingContext subContext, 
            IEnumerable<KeyValuePair<PotentialParseParentNode, ParsingContext>> @switch,
            IEnumerable<(PotentialParseParentNode next, PotentialParseParentNode current)> nextToCurrentNodeMapping)
            : base(current, subContext, nextToCurrentNodeMapping)
        {
            this.Switch = @switch.ToDictionary(kvp => kvp.Key, kvp => kvp.Value ?? throw new ArgumentNullException(nameof(@switch)), NodeComparer);

            Invariant.Require(this.SubContext.Nodes.SetEquals(this.Switch.Keys));
        }
        
        public IReadOnlyDictionary<PotentialParseParentNode, ParsingContext> Switch { get; }

        protected override IEnumerable<ParsingContext> GetNextContexts() => this.Switch.Values;
    }
}
