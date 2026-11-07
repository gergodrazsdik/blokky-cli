using System.Collections.Frozen;

namespace Blokky.Core.Features;

/// <summary>
/// The feature ids this build supports (architecture.md §3). Per build, never per config: no flag or key
/// enables an unsupported feature. The conformance adapter declares support from this table (CF-12, CF-54),
/// so the runner's skips and the binary's behaviour can't drift.
/// </summary>
public static class FeatureSupport
{
    /// <summary>
    /// Supported feature ids. Empty until the validator and run engine land (T-0069, T-0070): every plan is
    /// rejected with <c>BK_PLAN_NOT_SUPPORTED</c>.
    /// </summary>
    public static FrozenSet<string> Supported { get; } = FrozenSet<string>.Empty;

    /// <summary>Whether this build supports <paramref name="featureId"/>.</summary>
    public static bool IsSupported(string featureId) => Supported.Contains(featureId);
}
