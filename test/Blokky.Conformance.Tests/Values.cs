using Blokky.Conformance.Yaml;

namespace Blokky.Conformance.Tests;

/// <summary>Builds expectations and results in the value model from YAML text.</summary>
internal static class Values
{
    public static object? Yaml(string text) => YamlValues.Load(text);

    public static OrderedDictionary<string, object?> Map(string text) => (OrderedDictionary<string, object?>)YamlValues.Load(text)!;
}
