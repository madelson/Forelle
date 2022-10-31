using Medallion.Collections;
using System.Net;
using System.Text;

namespace ForellePlayground.Tests.LRInlining;

internal class DebuggingHelpers
{
    public static string CreateLRTableHtml(IEnumerable<LRState> rootStates)
    {
        HashSet<LRState> allStates = new();
        foreach (var state in rootStates.SelectMany(
            s => Traverse.DepthFirst(
                s, 
                st => st.SymbolsWithActions.SelectMany(
                    sy => st.GetActions(sy).ToArray().OfType<Shift>().Select(sh => sh.Destination).Where(allStates.Add)))))
        {
            allStates.Add(state);
        }
        var allTransitionSymbols = allStates.SelectMany(s => s.SymbolsWithActions)
            .Distinct()
            .OrderBy(s => s is NonTerminal)
            .ThenBy(s => s.Name)
            .ToArray();

        StringBuilder builder = new();
        builder.AppendLine("<table>")
            .AppendLine("<tr>")
            .AppendLine("<th>ID</th>");
        foreach (var symbol in allTransitionSymbols)
        {
            builder.AppendLine($"<th>{WebUtility.HtmlEncode(symbol.ToString())}</th>");
        }
        builder.AppendLine("<th>State</th>");
        builder.AppendLine("</tr>");

        foreach (var state in allStates.OrderBy(s => s.Id))
        {
            builder.AppendLine("<tr>")
                .AppendLine($"<td>{state.Id}</td>");
            foreach (var symbol in allTransitionSymbols)
            {
                builder.Append("<td>")
                    .Append(string.Join(",", state.GetActions(symbol).ToArray().Select(
                        a => a is Shift shift ? $"{(symbol is Token ? "s" : "g")}{shift.Destination.Id}"
                            : a is Reduce reduce ? $"r {{ {WebUtility.HtmlEncode(reduce.Rule.ToString())} }}"
                            : throw new InvalidOperationException()
                    )))
                    .AppendLine("</td>");
            }
            builder.Append("<td>")
                .Append(WebUtility.HtmlEncode(ToString(state)))
                .AppendLine("</td>")
                .AppendLine("</tr>");
        }

        builder.AppendLine("</table>");

        return builder.ToString();

        static string ToString(LRState state)
        {
            StringBuilder builder = new();
            builder.Append("{ ");
            foreach (var item in state.ItemsList.OrderByDescending(i => i.Rule.Position).ThenByDescending(i => i.Rule.RemainingSymbols.Length).ThenBy(i => i.Rule.ToString()))
            {
                builder.Append('[')
                    .Append(item)
                    .Append("]; ");
            }
            builder.Append('}');
            return builder.ToString();
        }
    }
}
