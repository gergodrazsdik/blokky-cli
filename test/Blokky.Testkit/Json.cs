using System.Text.Encodings.Web;
using System.Text.Json;

namespace Blokky.Testkit;

/// <summary>Compact JSON lines (TK-5, TK-12, TK-13, TK-16, TK-17), written with Utf8JsonWriter (no reflection).</summary>
internal static class Json
{
    private static readonly JsonWriterOptions Options = new()
    {
        Indented = false,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        SkipValidation = false,
    };

    /// <summary>Builds one JSON value followed by an LF.</summary>
    public static byte[] Line(Action<Utf8JsonWriter> write)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer, Options))
        {
            write(writer);
        }

        buffer.WriteByte((byte)'\n');
        return buffer.ToArray();
    }

    public static void StringArray(Utf8JsonWriter writer, IEnumerable<string> values)
    {
        writer.WriteStartArray();
        foreach (var value in values)
        {
            writer.WriteStringValue(value);
        }

        writer.WriteEndArray();
    }
}
