using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text;

namespace Forelle.Parsing
{
    internal interface IFirstFollowProvider
    {
        ImmutableHashSet<Token> FirstOf(Symbol symbol);
        ImmutableHashSet<Token> FollowOf(Symbol symbol);
        ImmutableHashSet<Token> FollowOf(Rule rule);
    }
}
