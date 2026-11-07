using System.Collections.Frozen;

namespace Blokky.Core.Features;

/// <summary>Which of the two feature tables applies (ADR 0009).</summary>
public enum FeatureTable
{
    /// <summary>Features the validator fully checks; <c>validate</c> gates on it.</summary>
    Validated,

    /// <summary>Features the run engine executes; <c>run</c> gates on it, after <see cref="Validated"/>.</summary>
    Runnable,
}

/// <summary>
/// The feature ids this build supports (architecture.md §3, ADR 0009), in two tables: <see cref="Validated"/> and
/// <see cref="Runnable"/>, with runnable always a subset of validated. Per build, never per config: no flag or key
/// enables an unsupported feature. The conformance adapter declares validated for validation cases and runnable for run
/// cases (CF-12, CF-54), so the runner's skips and the binary's behaviour can't drift.
/// </summary>
public static class FeatureSupport
{
    /// <summary>Features the validator fully checks. Empty until T-0081 to T-0086 grow it to the 0.1 table.</summary>
    public static FrozenSet<string> Validated { get; } = FrozenSet<string>.Empty;

    /// <summary>Features the run engine executes, a subset of <see cref="Validated"/>. Empty until T-0070.</summary>
    public static FrozenSet<string> Runnable { get; } = FrozenSet<string>.Empty;

    /// <summary>The table <paramref name="table"/>.</summary>
    public static FrozenSet<string> For(FeatureTable table) => table switch
    {
        FeatureTable.Validated => Validated,
        FeatureTable.Runnable => Runnable,
        _ => throw new ArgumentOutOfRangeException(nameof(table), table, "unknown feature table"),
    };

    /// <summary>Whether the validator fully checks <paramref name="featureId"/>.</summary>
    public static bool IsValidated(string featureId) => Validated.Contains(featureId);

    /// <summary>Whether the run engine executes <paramref name="featureId"/>.</summary>
    public static bool IsRunnable(string featureId) => Runnable.Contains(featureId);
}
