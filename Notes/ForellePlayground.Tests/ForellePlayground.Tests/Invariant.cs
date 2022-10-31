using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace ForellePlayground.Tests;

internal static class Invariant
{
    [Conditional("DEBUG")]
    public static void Require(bool condition, [CallerArgumentExpression("condition")] string? message = null)
    {
        if (!condition) { throw new InvalidOperationException(message); }
    }
}
