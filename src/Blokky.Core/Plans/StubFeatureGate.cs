using System.Text.Json.Nodes;
using Blokky.Core.Errors;
using Blokky.Core.Features;

namespace Blokky.Core.Plans;

/// <summary>
/// Stand-in for the validator until T-0069: with an empty <see cref="FeatureSupport"/> table every plan is
/// not supported. It reports one <c>BK_PLAN_NOT_SUPPORTED</c> naming the root node's kind, found by a
/// line scan for the first top-level kind key (no YAML parsing). If no kind key is found, it names
/// <see cref="FallbackFeature"/>. It never claims a plan is valid, and nothing runs.
/// </summary>
public static class StubFeatureGate
{
    /// <summary>The feature named when the scan finds no top-level kind key.</summary>
    public const string FallbackFeature = "exec";

    /// <summary>Checks <paramref name="plan"/>; returns the plan errors (never empty while nothing is supported).</summary>
    public static IReadOnlyList<BlokkyError> Check(PlanSource plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var (feature, line) = FindRootKind(plan.Bytes.Span);
        if (FeatureSupport.IsSupported(feature))
        {
            // Unreachable while the table is empty; the real validator replaces this class first.
            throw new InvalidOperationException("the stub gate can't validate supported features");
        }

        return
        [
            new BlokkyError
            {
                Code = ErrorCodes.PlanNotSupported,
                Message = $"this build of blokky doesn't support '{feature}' yet: nothing was run",
                Node = line is null ? null : "/",
                Pos = line is null ? null : BlokkyError.FormatPos(line.Value, 1),
                Detail = new JsonObject { ["feature"] = feature },
            },
        ];
    }

    /// <summary>
    /// Finds the first line that starts in column 1 with a kind key followed by <c>:</c>.
    /// <c>exec</c> with a scalar on the same line that isn't a flow sequence is <c>exec-string</c>.
    /// </summary>
    internal static (string Feature, int? Line) FindRootKind(ReadOnlySpan<byte> bytes)
    {
        var lineNumber = 0;
        while (!bytes.IsEmpty)
        {
            lineNumber++;
            var end = bytes.IndexOfAny((byte)'\n', (byte)'\r');
            var line = end < 0 ? bytes : bytes[..end];
            if (lineNumber == 1 && line.StartsWith(Utf8Bom))
            {
                line = line[3..];
            }

            var kind = KindAt(line);
            if (kind is not null)
            {
                return (kind, lineNumber);
            }

            if (end < 0)
            {
                break;
            }

            var next = end + (bytes[end] == '\r' && end + 1 < bytes.Length && bytes[end + 1] == '\n' ? 2 : 1);
            bytes = bytes[next..];
        }

        return (FallbackFeature, null);
    }

    private static ReadOnlySpan<byte> Utf8Bom => [0xEF, 0xBB, 0xBF];

    private static string? KindAt(ReadOnlySpan<byte> line)
    {
        var colon = line.IndexOf((byte)':');
        if (colon is <= 0 or > MaxKindLength)
        {
            return null;
        }

        var key = System.Text.Encoding.UTF8.GetString(line[..colon]);
        if (key == "exec-string" || !FeatureIds.Kinds.Contains(key, StringComparer.Ordinal))
        {
            return null;
        }

        if (key == "exec")
        {
            var value = line[(colon + 1)..].Trim(" \t"u8);
            var isString = !value.IsEmpty && value[0] != '[' && value[0] != '#';
            return isString ? "exec-string" : "exec";
        }

        return key;
    }

    private const int MaxKindLength = 16;
}
