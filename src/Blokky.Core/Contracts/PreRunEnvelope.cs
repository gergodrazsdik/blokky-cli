using System.Text.Json.Serialization;
using Blokky.Core.Errors;

namespace Blokky.Core.Contracts;

/// <summary>
/// What stdout carries when a run fails before it starts (output-contract.md §4, OC-11 to OC-13).
/// </summary>
public sealed record PreRunEnvelope
{
    /// <summary>The contract version shared by the report, the envelope and the event log (OC-24).</summary>
    public const string CurrentSchemaVersion = "0.1";

    /// <summary>Creates an envelope for the failing step and its errors.</summary>
    public PreRunEnvelope(Phase phase, IReadOnlyList<BlokkyError> errors)
    {
        ArgumentNullException.ThrowIfNull(errors);
        if (errors.Count == 0)
        {
            throw new ArgumentException("a pre-run envelope carries at least one error", nameof(errors));
        }

        Phase = phase;
        Errors = errors;
    }

    /// <summary>The contract version.</summary>
    public string SchemaVersion { get; } = CurrentSchemaVersion;

    /// <summary>Always <c>invalid</c>.</summary>
    public string Status { get; } = "invalid";

    /// <summary>The first failing step.</summary>
    public Phase Phase { get; }

    /// <summary>Every error the step found.</summary>
    public IReadOnlyList<BlokkyError> Errors { get; }

    /// <summary>The exit code for this envelope (not serialized).</summary>
    [JsonIgnore]
    public int ExitCode => ExitCodes.ForPhase(Phase, Errors);
}
