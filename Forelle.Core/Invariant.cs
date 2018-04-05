using System;
using System.Collections.Generic;
using System.Text;

namespace Forelle
{
    internal static class Invariant
    {
        public static void Require(bool condition, string message = null)
        {
            if (!condition)
            {
                throw new InvariantViolatedException(message);
            }
        }
    }

    internal sealed class InvariantViolatedException : Exception
    {
        public InvariantViolatedException(string message)
        {
        }
    }
}
