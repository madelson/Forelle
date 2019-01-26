using Forelle.Parsing;
using Forelle.Parsing.Construction.New2;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Forelle.Tests.Parsing.Construction
{
    internal static class ParserPrinter
    {
        public static string ToString(
            IReadOnlyDictionary<StartSymbolInfo, ParsingContext> startContexts, 
            IReadOnlyDictionary<ParsingContext, ParsingAction> contextActions)
        {
            var contextIds = new Dictionary<ParsingContext, int>();

            var builder = new StringBuilder();
            
            foreach (var kvp in startContexts)
            {
                builder.AppendLine($"To parse '{kvp.Key.Symbol}', GOTO {IdOf(kvp.Value)}");
            }
            builder.AppendLine();

            foreach (var context in contextActions.Keys.OrderBy(IdOf))
            {
                builder.AppendLine($"==== CONTEXT {IdOf(context)} ====");
                foreach (var node in context.Nodes.OrderBy(n => n.ToString()))
                {
                    builder.AppendLine(node.ToMarkedString());
                }
                builder.AppendLine($"LOOKAHEAD: {TokensToString(context.LookaheadTokens)}")
                    .AppendLine();

                switch (contextActions[context])
                {
                    case EatTokenAction eatToken:
                        builder.AppendLine($"SHIFT '{eatToken.Token}', THEN GOTO {IdOf(eatToken.Next)}");
                        break;
                    case ReduceAction reduce:
                        builder.AppendLine($"REDUCE BY {string.Join(" | ", reduce.Parses)}");
                        break;
                    case ParseContextAction parseContext:
                        builder.AppendLine($"PARSE {IdOf(parseContext.Context)}, THEN GOTO {IdOf(parseContext.Next)}");
                        break;
                    case TokenSwitchAction tokenSwitch:
                        foreach (var tokenSet in tokenSwitch.Switch.GroupBy(kvp => kvp.Value, kvp => kvp.Key))
                        {
                            builder.AppendLine($"ON {TokensToString(tokenSet.ToArray())}, GOTO {IdOf(tokenSet.Key)}");
                        }
                        break;
                    case DelegateToContextAction delegateTo:
                        builder.AppendLine($"GOTO {IdOf(delegateTo.Next)}");
                        break;
                    default:
                        throw new NotImplementedException();
                }

                builder.AppendLine();
            }

            return builder.ToString();

            int IdOf(ParsingContext context)
            {
                if (!contextIds.ContainsKey(context))
                {
                    contextIds.Add(context, contextIds.Count);
                }
                return contextIds[context];
            }

            string TokenToString(Token token) => $"'{token}'";

            string TokensToString(IReadOnlyCollection<Token> tokens)
            {
                return tokens.Count == 1
                    ? TokenToString(tokens.Single())
                    : $"[{string.Join(", ", tokens.OrderBy(t => t.Name).Select(TokenToString))}]";
            }
        }
    }
}
