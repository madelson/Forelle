namespace ForellePlayground.Tests;

internal static class TestHelpers
{
    public static T ShouldEqual<T>(this T actual, T expected)
    {
        Assert.That(actual, Is.EqualTo(expected));
        return actual;
    }
}
