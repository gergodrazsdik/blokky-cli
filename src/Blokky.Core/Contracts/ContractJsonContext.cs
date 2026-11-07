using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Blokky.Core.Contracts;

/// <summary>
/// Source-generated serialization for the pre-run envelope (tech-stack.md §3). Snake-case names, nulls omitted.
/// The run report, events and <c>leaves</c> get their own contexts as they land.
/// </summary>
[JsonSerializable(typeof(PreRunEnvelope))]
internal sealed partial class ContractJsonContext : JsonSerializerContext
{
    /// <summary>
    /// The context stdout uses. JSON is never embedded in HTML here, so only what JSON requires is escaped
    /// (quotes, backslashes, control characters) and messages stay readable: <c>doesn't</c>, not <c>doesn\u0027t</c>.
    /// </summary>
    public static ContractJsonContext Contract { get; } = new(new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    });
}
