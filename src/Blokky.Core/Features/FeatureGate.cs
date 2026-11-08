using Blokky.Core.Validation;

namespace Blokky.Core.Features;

/// <summary>
/// The feature-support gate (architecture.md §3, ADR 0009): every feature a plan uses that isn't in the table its command
/// gates on is a <c>BK_PLAN_NOT_SUPPORTED</c> error, at the feature's first use, with <c>detail.feature</c> (SY-31, SY-94).
/// It runs after the schema-level checks and the static rules, and its errors are reported with theirs.
/// </summary>
public static class FeatureGate
{
    /// <summary>Reports the features of <paramref name="uses"/> that <paramref name="supported"/> lacks.</summary>
    /// <param name="uses">The plan's features (<see cref="FeatureDeriver"/>).</param>
    /// <param name="supported">The table the command gates on: validated for <c>validate</c>, runnable for <c>run</c>.</param>
    /// <param name="table">Which table, for the message.</param>
    /// <param name="errors">Where the errors go.</param>
    public static void Check(IReadOnlyList<FeatureUse> uses, IReadOnlySet<string> supported, FeatureTable table, PlanErrorSink errors)
    {
        ArgumentNullException.ThrowIfNull(uses);
        ArgumentNullException.ThrowIfNull(supported);
        ArgumentNullException.ThrowIfNull(errors);
        foreach (var use in uses)
        {
            if (!supported.Contains(use.Feature))
            {
                errors.NotSupported(use.Feature, use.Position, use.Node, table == FeatureTable.Validated ? "validate" : "run");
            }
        }
    }
}
