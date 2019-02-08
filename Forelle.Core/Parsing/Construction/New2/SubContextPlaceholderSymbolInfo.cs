using System;
using System.Collections.Generic;
using System.Text;
using Medallion.Collections;

namespace Forelle.Parsing.Construction.New2
{
    /// <summary>
    /// Stands in for a sub-context that gets moved past in a <see cref="SubContextSwitchAction"/>
    /// </summary>
    internal class SubContextPlaceholderSymbolInfo : SyntheticSymbolInfo
    {
        private SubContextPlaceholderSymbolInfo() { }

        public class Factory
        {
            private readonly Dictionary<PotentialParseParentNode, PotentialParseParentNode> _placeholders =
                new Dictionary<PotentialParseParentNode, PotentialParseParentNode>(PotentialParseNodeWithCursorComparer.Instance);

            public PotentialParseParentNode GetPlaceholderNode(PotentialParseParentNode replaced)
            {
                Invariant.Require(replaced.CursorPosition.HasValue);

                return this._placeholders.GetOrAdd(
                    replaced,
                    r =>
                    {
                        var nonTerminal = NonTerminal.CreateSynthetic($"Placeholder<{r} @ {r.GetCursorLeafIndex()}>", new SubContextPlaceholderSymbolInfo());
                        return new PotentialParseParentNode(
                            new Rule(replaced.Symbol, new[] { nonTerminal }, ExtendedRuleInfo.Unmapped),
                            new[] { new PotentialParseLeafNode(nonTerminal, cursorPosition: 1) }
                        );
                    }
                );
            }
        }
    }
}
