using System.Text.Json;

namespace Blokky.Conformance.Execution;

/// <summary>Converts JSON into the value model the matcher reads (the same as <see cref="Yaml.YamlValues"/>).</summary>
internal static class JsonValues
{
    public static object? From(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Object => Object(element),
        JsonValueKind.Array => element.EnumerateArray().Select(From).ToList(),
        JsonValueKind.String => element.GetString(),
        JsonValueKind.Number => element.TryGetInt64(out var l) ? (object)l : element.GetDouble(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        _ => null,
    };

    private static OrderedDictionary<string, object?> Object(JsonElement element)
    {
        var map = new OrderedDictionary<string, object?>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            map[property.Name] = From(property.Value);
        }

        return map;
    }
}
