namespace Blokky.Core.Contracts;

/// <summary>The plan a verdict is about: the run report's <c>plan</c> field (output-contract.md §3.2).</summary>
/// <param name="Sha256">SHA-256 of the plan bytes, lowercase hex.</param>
/// <param name="SpecVersion">The spec version from <c>blokky:</c>.</param>
/// <param name="Source"><c>stdin</c> or the plan file's absolute path.</param>
public sealed record PlanIdentity(string Sha256, string SpecVersion, string Source);

/// <summary>
/// What stdout carries when <c>blokky validate</c> accepts a plan (cli.md "validate output", LV-29): the verdict, exit 0.
/// Without a configured validator (none exists before 0.4) the verdict is <c>allow</c>. A rejected plan gets the pre-run
/// envelope instead (<see cref="PreRunEnvelope"/>).
/// </summary>
public sealed record ValidateVerdict
{
    /// <summary>Creates the verdict for <paramref name="plan"/>.</summary>
    public ValidateVerdict(PlanIdentity plan) => Plan = plan;

    /// <summary>The contract version (OC-24).</summary>
    public string SchemaVersion { get; } = PreRunEnvelope.CurrentSchemaVersion;

    /// <summary>Always <c>valid</c>: the counterpart of the envelope's <c>invalid</c>.</summary>
    public string Status { get; } = "valid";

    /// <summary><c>allow</c>; <c>ask</c> and <c>deny</c> come with the validator (LV-27).</summary>
    public string Decision { get; } = "allow";

    /// <summary>The plan validated.</summary>
    public PlanIdentity Plan { get; }
}
