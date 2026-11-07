namespace Blokky.Conformance.Tests;

/// <summary>Case files under <c>Fixtures/</c>, copied next to the test assembly.</summary>
internal static class Fixtures
{
    public static string Read(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));
}
