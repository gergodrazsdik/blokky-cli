namespace Blokky.Core.Plans;

/// <summary>The raw bytes of a plan and where they came from.</summary>
/// <param name="Bytes">The plan bytes, unchanged.</param>
/// <param name="Source"><c>stdin</c>, or the plan file's absolute path (the report's <c>plan.source</c>).</param>
public sealed record PlanSource(ReadOnlyMemory<byte> Bytes, string Source)
{
    /// <summary>The <see cref="Source"/> value for a plan read from standard input.</summary>
    public const string Stdin = "stdin";
}
