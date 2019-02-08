using Medallion.Collections;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;

namespace Forelle.Parsing.Construction.New2
{
    /// <summary>
    /// Converts <see cref="UnresolvedSubContextSwitchAction"/>s to <see cref="SubContextSwitchAction"/>s
    /// </summary>
    internal class UnresolvedSubContextSwitchActionResolver
    {
        private static readonly IEqualityComparer<PotentialParseParentNode> NodeComparer = PotentialParseNodeWithCursorComparer.Instance;
        private static readonly IEqualityComparer<ImmutableHashSet<PotentialParseParentNode>> NodeSetComparer = ImmutableHashSetComparer<PotentialParseParentNode>.Instance;
        private static readonly ImmutableArray<bool> RequireResolutionValues = ImmutableArray.Create(true, false);
        
        private readonly IReadOnlyCollection<ParsingContext> _startContexts;
        private ImmutableDictionary<ParsingContext, ParsingAction> _parsingActions;

        private UnresolvedSubContextSwitchActionResolver(
            ImmutableDictionary<ParsingContext, ParsingAction> parsingActions,
            IReadOnlyCollection<ParsingContext> startContexts)
        {
            this._parsingActions = parsingActions;
            this._startContexts = startContexts;
        }

        public static bool TryResolvePotentialSubParseSwitches(
            ImmutableDictionary<ParsingContext, ParsingAction> parsingActions, 
            IReadOnlyCollection<ParsingContext> startContexts, 
            out ImmutableDictionary<ParsingContext, ParsingAction> resolvedActions)
        {
            var resolver = new UnresolvedSubContextSwitchActionResolver(parsingActions, startContexts);
            while (resolver.TryResolveOne()) ;

            resolvedActions = resolver._parsingActions;
            return true;
        }

        private bool TryResolveOne()
        {
            // first we try to get a resolution for all contexts requiring one. Then we go back and 
            // resolve contexts that don't require a resolution. The reason for this ordering is that
            // if we are forced to apply an ambiguity resolution to a required resolution context then
            // we might be able to benefit from that when resolving another context
            foreach (var requireResolution in RequireResolutionValues)
            {
                var toResolve = this.GetNextReachableActionToResolveOrDefault(requireResolution);
                if (toResolve != null)
                {
                    this._parsingActions = this._parsingActions.SetItem(toResolve.Current, this.Resolve(toResolve, requireResolution));
                    return true;
                }
            }

            return false;
        }

        private ParsingAction Resolve(UnresolvedSubContextSwitchAction toResolve, bool requireResolution)
        {
            // note: adding to visited is handled by GetDifferentiableSets(context, visited), so when we call 
            // GetDifferentiableSets(unresolvedSubContextSwitch, visited) directly we must pre-populate it
            var result = this.GetDifferentiableSets(toResolve, visited: ImmutableHashSet.Create(toResolve.Current));
            if (requireResolution && result.differentiableSets.Any(s => s.Count != 1))
            {
                throw new NotImplementedException("needs ambiguity res");
            }

            // todo I'd like to do this but right now we don't allow it...
            //// if all switch cases lead to the same outcome, we don't need to switch at all
            //if (result.@switch.Select(kvp => kvp.Value).Distinct().Count() == 1)
            //{
            //    return new ParseSubContextAction(
            //        toResolve.Current,
            //        toResolve.SubContext,
            //        result.@switch.Only(kvp => kvp.Value)
            //    );
            //}

            return new SubContextSwitchAction(
                toResolve.Current,
                subContext: toResolve.SubContext,
                @switch: result.@switch.SelectMany(kvp => kvp.Key, (kvp, node) => (node, context: kvp.Value))
                    .ToDictionary(t => t.node, t => t.context),
                toResolve.NextToCurrentNodeMapping.SelectMany(kvp => kvp.Value, (kvp, current) => (next: kvp.Key, current))
            );
        }

        /// <summary>
        /// For an <paramref name="unresolvedSubContextSwitch"/>, returns both its differentiable sets 
        /// (see <see cref="GetDifferentiableSets(ParsingContext, ImmutableHashSet{ParsingContext})"/>)
        /// and the mapping between the result of the recursive sub context and which next context we
        /// will use
        /// </summary>
        private (ImmutableHashSet<ImmutableHashSet<PotentialParseParentNode>> differentiableSets, Dictionary<ImmutableHashSet<PotentialParseParentNode>, ParsingContext> @switch) GetDifferentiableSets(
            UnresolvedSubContextSwitchAction unresolvedSubContextSwitch,
            ImmutableHashSet<ParsingContext> visited)
        {
            // first, see how the subparse differentiates
            var subParseDifferentiableSets = this.GetDifferentiableSets(unresolvedSubContextSwitch.SubContext, visited);
            var differentiableNextContexts = subParseDifferentiableSets.Select(
                    s => (
                        set: s,
                        mapped: s.Join(
                                unresolvedSubContextSwitch.Switch,
                                n => n,
                                t => t.subContextNode,
                                (n, t) => t.nextNode,
                                NodeComparer
                            )
                            .ToArray()
                    )
                )
                .ToDictionary(
                    t => t.set,
                    t => unresolvedSubContextSwitch.NextContexts.SingleOrDefault(c => c.Nodes.SetEquals(t.mapped))
                );
            if (differentiableNextContexts.ContainsValue(null))
            {
                // this indicates that the context we are being guided to by the recursive context is one that we simply cannot solve
                return (ImmutableHashSet.Create(NodeSetComparer, unresolvedSubContextSwitch.Current.Nodes), differentiableNextContexts);
            }

            // get the differentiable sets for each next context we're going to use
            var differentiableNextContextResults = differentiableNextContexts.Values
                .Select(c => this.GetDifferentiableSets(c, visited));
            return (
                MapNextContextDifferentiableSetsToCurrent(MergeDifferentiableSets(differentiableNextContextResults), unresolvedSubContextSwitch), 
                differentiableNextContexts
            );
        }

        /// <summary>
        /// Given a <paramref name="context"/> and list of <paramref name="visited"/> contexts,
        /// returns a set of sets of nodes indicating the potential results we might get from parsing
        /// <paramref name="context"/>. 
        /// 
        /// The reason we return a set of sets rather than just a set of nodes is to handle reduce-reduce
        /// ambiguity. For example, if we encounter a <see cref="ReduceAction"/> with multiple reductions
        /// we'll return one set of that set of reductions because when we reach that action we won't be
        /// able to differentiate between the reductions.
        /// </summary>
        private ImmutableHashSet<ImmutableHashSet<PotentialParseParentNode>> GetDifferentiableSets(
            ParsingContext context, 
            ImmutableHashSet<ParsingContext> visited)
        {
            // if we're down to one node, then by definition we can fully differentiate between
            // the nodes in context
            if (context.Nodes.Count == 1) { return GetFullyDifferentiatedSets(); }

            var visitedWithContext = visited.Add(context);
            if (visitedWithContext == visited)
            {
                // if we encounter a node recursively, return full differentiation because 
                // it can't make things any worse. In other words, if a node's results are
                // the worst of its own results and non-recursive results, this is just
                // non-recursive results
                return GetFullyDifferentiatedSets();
            }
            
            // special handling for unresolved switches & reductions

            var action = this._parsingActions[context];
            if (action is UnresolvedSubContextSwitchAction potentialSubParseSwitch)
            {
                return this.GetDifferentiableSets(potentialSubParseSwitch, visitedWithContext).differentiableSets;
            }

            if (action is ReduceAction reduce)
            {
                return ImmutableHashSet.Create(NodeSetComparer, reduce.Parses);
            }

            // otherwise, we simply consider the action's next contexts
            var nextContextResults = action.NextContexts.Select(c => this.GetDifferentiableSets(c, visitedWithContext));
            return MapNextContextDifferentiableSetsToCurrent(MergeDifferentiableSets(nextContextResults), action);

            ImmutableHashSet<ImmutableHashSet<PotentialParseParentNode>> GetFullyDifferentiatedSets() => context.Nodes
                .Select(n => ImmutableHashSet.Create(NodeComparer, n))
                .ToImmutableHashSet(NodeSetComparer);
        }

        /// <summary>
        /// The merger of differentiable sets is just the union of the sets of set, eliminating any set
        /// which is a subset of another set in the result. So for example if the input were { { 1, 2 }, { 3 } }
        /// and { { 1 }, { 2, 3 } } then the output would be { { 1, 2 }, { 2, 3 } }
        /// </summary>
        private static ImmutableHashSet<ImmutableHashSet<PotentialParseParentNode>> MergeDifferentiableSets(
            IEnumerable<ImmutableHashSet<ImmutableHashSet<PotentialParseParentNode>>> setsOfSets)
        {
            var result = ImmutableHashSet.CreateBuilder(NodeSetComparer);
            foreach (var set in setsOfSets.SelectMany(sets => sets).OrderByDescending(s => s.Count))
            {
                if (!result.Any(set.IsSubsetOf))
                {
                    result.Add(set);
                }
            }

            return result.ToImmutable();
        }

        /// <summary>
        /// Given the differentiable sets of <paramref name="current"/>'s next contexts, returns differentiable
        /// sets for <paramref name="current"/> by mapping the nodes back using 
        /// <see cref="ParsingAction.NextToCurrentNodeMapping"/>
        /// </summary>
        private static ImmutableHashSet<ImmutableHashSet<PotentialParseParentNode>> MapNextContextDifferentiableSetsToCurrent(
            ImmutableHashSet<ImmutableHashSet<PotentialParseParentNode>> nextContextDifferentiableSets,
            ParsingAction current)
        {
            return nextContextDifferentiableSets.Select(
                    s => s.SelectMany(n => current.NextToCurrentNodeMapping[n]).ToImmutableHashSet(s.KeyComparer)
                )
                .ToImmutableHashSet(nextContextDifferentiableSets.KeyComparer);
        }
        
        /// <summary>
        /// Starting from <see cref="_startContexts"/>, finds the first (in BFS order) reachable 
        /// <see cref="UnresolvedSubContextSwitchAction"/> (or null if none is found).
        /// 
        /// The <paramref name="requireResolution"/> parameter determines whether we explore the full tree
        /// or just those <see cref="ParsingAction"/>s which are needed to drive parsing decisions
        /// </summary>
        private UnresolvedSubContextSwitchAction GetNextReachableActionToResolveOrDefault(bool requireResolution)
        {
            var visited = new HashSet<ParsingContext>();
            return Traverse.BreadthFirst(
                    default(ParsingContext),
                    c =>
                    {
                        // boostrap the search
                        if (c == null) { return this._startContexts; }
                        // don't re-visit a context. Since this function isn't contextual, we'll always get the same result.
                        if (!visited.Add(c)) { return Enumerable.Empty<ParsingContext>(); }
                        var action = this._parsingActions[c];
                        // don't recurse into unresolved contexts. We don't know what of their next contexts will be reachable
                        // and we don't know whether the sub context will require resolution yet. We'll eventually get to all
                        // of these since they'll get converted to resolved actions
                        if (action is UnresolvedSubContextSwitchAction) { return Enumerable.Empty<ParsingContext>(); }
                        // if we only want actions that NEED a resolution, don't recurse into sub contexts. These are the
                        // only cases where we will tolerate a "look past" resolution
                        if (requireResolution && action is SubContextSwitchAction) { return action.NextContexts; }
                        return action.ReferencedContexts;
                    }
                )
                .Select(c => c != null ? this._parsingActions[c] : null)
                .OfType<UnresolvedSubContextSwitchAction>()
                .FirstOrDefault();
        }
    }
}
