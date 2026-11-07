using System.Text.Json.Serialization;

namespace Blokky.Core.Errors;

/// <summary>The step of a pre-run check that failed (output-contract.md OC-11, cli.md precedence).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<Phase>))]
public enum Phase
{
    /// <summary>Arguments, flags and input values.</summary>
    [JsonStringEnumMemberName("usage")]
    Usage,

    /// <summary>The plan: encoding, YAML, schema, static rules, feature gate, lint.</summary>
    [JsonStringEnumMemberName("plan")]
    Plan,

    /// <summary>Configuration.</summary>
    [JsonStringEnumMemberName("config")]
    Config,

    /// <summary><c>--expect-sha256</c> and approval.</summary>
    [JsonStringEnumMemberName("approval")]
    Approval,
}
