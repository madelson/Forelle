using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;

namespace Forelle.Parsing
{
    internal interface IFirstFollowProvider
    {
        ImmutableHashSet<Token> FirstOf(Symbol symbol);
        ImmutableHashSet<Token> FollowOf(Symbol symbol);
        ImmutableHashSet<Token> FollowOf(Rule rule);
    }

    internal static class FirstFollowProviderExtensions
    {
        public static bool IsNullable(this IFirstFollowProvider provider, Symbol symbol)
        {
            return symbol is NonTerminal && provider.FirstOf(symbol).Contains(null);
        }

        public static ImmutableHashSet<Token> FirstOf(this IFirstFollowProvider provider, IEnumerable<Symbol> symbols)
        {
            var builder = ImmutableHashSet.CreateBuilder<Token>();
            foreach (var symbol in symbols)
            {
                var firsts = provider.FirstOf(symbol);
                builder.UnionWith(firsts.Where(s => s != null));
                if (!firsts.Contains(null))
                {
                    // not nullable
                    return builder.ToImmutable();
                }
            }

            // if we reach here, we're nullable
            builder.Add(null);
            return builder.ToImmutable();
        }

        public static ImmutableHashSet<Token> NextOf(this IFirstFollowProvider provider, Rule rule)
        {
            return provider.NextOf(rule.Skip(0));
        }

        public static ImmutableHashSet<Token> NextOf(this IFirstFollowProvider provider, RuleRemainder ruleRemainder)
        {
            var firsts = provider.FirstOf(ruleRemainder.Symbols);
            return firsts.Contains(null)
                ? firsts.Remove(null).Union(provider.FollowOf(ruleRemainder.Rule))
                : firsts;
        }
    }
}
