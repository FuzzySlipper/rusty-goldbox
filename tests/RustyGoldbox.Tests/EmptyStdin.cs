using System.Runtime.CompilerServices;

namespace RustyGoldbox.Tests;

/// <summary>Gives the in-process CLI an empty stdin, so `goldbox play` without a script never waits on the test runner's input.</summary>
internal static class EmptyStdin
{
    [ModuleInitializer]
    internal static void Install()
    {
        Console.SetIn(TextReader.Null);
    }
}
