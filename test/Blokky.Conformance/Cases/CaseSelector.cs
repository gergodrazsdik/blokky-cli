using System.Text;
using System.Text.RegularExpressions;
using Blokky.Conformance.Execution;

namespace Blokky.Conformance.Cases;

/// <summary>Why a case is skipped (CF-62).</summary>
internal enum SkipKind
{
    Platform,
    Requirement,
    NotSupported,
}

/// <summary>The selection for one case: a skip with its reason, or the clock mode to run in.</summary>
internal sealed record Selection(SkipKind? Skip, string? Reason, IReadOnlyList<string> Unsupported, string? Clock)
{
    public static Selection Run(string clock) => new(null, null, [], clock);

    public static Selection Skipped(SkipKind kind, string reason, IReadOnlyList<string>? unsupported = null) =>
        new(kind, reason, unsupported ?? [], null);
}

/// <summary>Applies CF-60's order: platform skips (CF-33), requirements (CF-13), features (CF-12), then the clock (CF-39).</summary>
internal static partial class CaseSelector
{
    /// <summary>Fastest first (CF-39).</summary>
    private static readonly string[] ClockPreference = ["virtual", "scaled", "real"];

    public static Selection Select(ConformanceCase c, HostInfo host, AdapterCapabilities capabilities)
    {
        ArgumentNullException.ThrowIfNull(c);
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(capabilities);
        if (c.PlatformSkip is { } why)
        {
            return Selection.Skipped(SkipKind.Platform, $"platform: {why}");
        }

        if (c.Requires.Os.Count > 0 && !c.Requires.Os.Contains(host.Os))
        {
            return Selection.Skipped(SkipKind.Requirement, "requirement: os");
        }

        if (c.Requires.Shells.FirstOrDefault(s => !host.Shells.ContainsKey(s)) is { } shell)
        {
            return Selection.Skipped(SkipKind.Requirement, $"requirement: shells ({shell} not installed)");
        }

        if (c.Requires.ContainerEngine && !host.ContainerEngine)
        {
            return Selection.Skipped(SkipKind.Requirement, "requirement: container-engine");
        }

        var unsupported = c.Features.Where(f => !capabilities.Features.Contains(f)).ToList();
        if (unsupported.Count > 0)
        {
            return Selection.Skipped(SkipKind.NotSupported, $"not supported: {string.Join(", ", unsupported)}", unsupported);
        }

        var clock = ClockPreference.FirstOrDefault(m => ClockModes(c).Contains(m) && capabilities.ClockModes.Contains(m));
        if (clock is null)
        {
            return Selection.Skipped(SkipKind.Requirement, "requirement: clock");
        }

        // The testkit is what cases run (CF-4, CF-58); without it no run can be checked.
        return host.Testkit is null
            ? Selection.Skipped(SkipKind.Requirement, "requirement: testkit (blokky-testkit isn't built; set --testkit or BLOKKY_TESTKIT)")
            : Selection.Run(clock);
    }

    /// <summary>
    /// The case's clock modes: its <c>clock</c>, or <c>[real, scaled]</c>; <c>[real]</c> for cases with <c>script</c>
    /// leaves or probes, or the testkit's <c>spawn</c>, <c>actions</c> or <c>after_run</c> (CF-39, CF-40).
    /// </summary>
    public static IReadOnlyList<string> ClockModes(ConformanceCase c)
    {
        ArgumentNullException.ThrowIfNull(c);
        if (c.Clock is { } explicitModes)
        {
            return explicitModes;
        }

        var realOnly = c.Features.Any(f => f is "script" or "cond-probe")
            || c.Actions.Count > 0
            || c.AfterRun.Count > 0
            || Spawn().IsMatch(Encoding.UTF8.GetString(c.Plan))
            || c.Programs.Values.Any(args => args.Contains("spawn"));
        return realOnly ? ["real"] : ["real", "scaled"];
    }

    [GeneratedRegex(@"blokky-testkit\s*,\s*['""]?spawn\b")]
    private static partial Regex Spawn();
}
