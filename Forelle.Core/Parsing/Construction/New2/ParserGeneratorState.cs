using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text;

namespace Forelle.Parsing.Construction.New2
{
    internal class ParserGeneratorState
    {
        public static ParserGeneratorState Empty { get; } = new ParserGeneratorState(ImmutableDictionary<ParsingContext, ParsingAction>.Empty, ImmutableHashSet<ParsingContext>.Empty);

        private ParserGeneratorState(ImmutableDictionary<ParsingContext, ParsingAction> solvedContexts, ImmutableHashSet<ParsingContext> solvingContexts)
        {
            this.SolvedContexts = solvedContexts;
            this.SolvingContexts = solvingContexts;
        }

        public ImmutableDictionary<ParsingContext, ParsingAction> SolvedContexts { get; }
        private ImmutableHashSet<ParsingContext> SolvingContexts { get; }
        
        public ParserGeneratorState AddSolution(ParsingContext context, ParsingAction action)
        {
            var newSolvingContexts = this.SolvingContexts.Remove(context);
            Invariant.Require(newSolvingContexts != this.SolvingContexts);

            return new ParserGeneratorState(this.SolvedContexts.Add(context, action), newSolvingContexts);
        }

        public ParserGeneratorState AddToSolve(ParsingContext context)
        {
            var newSolvingContexts = this.SolvingContexts.Add(context);
            return newSolvingContexts == this.SolvingContexts
                ? this
                : new ParserGeneratorState(this.SolvedContexts, newSolvingContexts);
        }
    }
}
