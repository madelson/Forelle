using System;
using System.Collections.Generic;
using System.Text;

namespace Forelle.Parsing.Preprocessing.LR
{
    internal abstract class LRAction
    {
    }

    internal sealed class LRShiftAction : LRAction
    {
        public LRShiftAction(LRClosure shifted)
        {
            this.Shifted = shifted;
        }

        public LRClosure Shifted { get; }
    }

    internal sealed class LRGotoAction : LRAction
    {
        public LRGotoAction(LRClosure @goto)
        {
            this.Goto = @goto;
        }

        public LRClosure Goto { get; }
    }

    internal sealed class LRReduceAction : LRAction
    {
        public LRReduceAction(Rule rule)
        {
            this.Rule = rule;
        }

        public Rule Rule { get; }
    }

    internal sealed class LRConflictAction : LRAction
    {
        public LRConflictAction(LRAction first, LRAction second)
        {
            var actions = new List<LRAction>();
            AddAction(first);
            AddAction(second);

            this.Actions = actions;

            void AddAction(LRAction action)
            {
                if (action is LRConflictAction conflict) { actions.AddRange(conflict.Actions); }
                else { actions.Add(action); }
            }
        }

        public IReadOnlyList<LRAction> Actions { get; }
    }
}
