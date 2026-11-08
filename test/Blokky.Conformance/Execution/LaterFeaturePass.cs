using Blokky.Conformance.Cases;
using Blokky.Conformance.Matching;
using Blokky.Core.Errors;

namespace Blokky.Conformance.Execution;

/// <summary>How a validation case did in the later-feature validation pass (ADR 0009).</summary>
internal enum LaterKind
{
    /// <summary>The expected errors were reported (<c>BK_PLAN_NOT_SUPPORTED</c> allowed in addition).</summary>
    Pass,

    /// <summary>Some expected errors belong to a pending rule family, and the rest of the outcome is right.</summary>
    Pending,

    /// <summary>A mismatch outside the pending rule families: it fails the run.</summary>
    Fail,

    /// <summary>The case can't be judged in this pass: it expects input errors, which follow the plan step.</summary>
    NotApplicable,
}

/// <summary>A case's result in the later-feature validation pass.</summary>
/// <param name="Kind">The outcome.</param>
/// <param name="Reason">The mismatch, or why the case is pending or not applicable.</param>
/// <param name="PendingKeys">The <see cref="PendingRuleFamilies"/> entries the case's expected errors fall under.</param>
internal sealed record LaterResult(LaterKind Kind, string? Reason, IReadOnlyList<string> PendingKeys);

/// <summary>An entry of the pending list: the rules a later task implements.</summary>
/// <param name="Key">A rule ID (<c>RF-3</c>), an error code (<c>BK_FROM_ENV_UNSET</c>) or a case id.</param>
/// <param name="Task">The task that implements it (T-0082 to T-0086).</param>
/// <param name="Why">What is missing.</param>
internal sealed record PendingEntry(string Key, string Task, string Why);

/// <summary>
/// The rule families not implemented yet, as an explicit, reviewed list: an expected error whose rule (or code, or case)
/// is listed may be missing from <c>blokky validate</c>'s output without failing the later-feature pass. Each of T-0082
/// to T-0086 deletes its entries; an entry whose cases all pass is reported as stale and fails the run, so the list only
/// shrinks. It is not an allow-list of failures: a pending case still fails when an expected error of an implemented rule
/// is missing, or (for an exhaustive case) when an error is reported that the case doesn't expect.
/// </summary>
internal static class PendingRuleFamilies
{
    public static IReadOnlyList<PendingEntry> Entries { get; } =
    [
        // T-0082: syntax rules on strings (exec strings, argv[0], regexes, JSON paths, globs, image references).
        new("SH-7", "T-0082", "exec string splitting"),
        new("SH-10", "T-0082", ".bat/.cmd as argv[0]"),
        new("RF-22", "T-0082", "the portable regex subset"),
        new("SY-65", "T-0082", "JSON path member names"),
        new("SY-66", "T-0082", "JSON path bracket members"),
        new("SY-67", "T-0082", "JSON path indexes"),
        new("SY-68", "T-0082", "unsupported JSON path forms"),
        new("SY-72", "T-0082", "glob ** segments"),
        new("SY-73", "T-0082", "glob braces"),
        new("SY-102", "T-0082", "glob .. segments"),
        new("SY-103", "T-0082", "image references"),

        // T-0083: variables and interpolation.
        new("RF-6", "T-0083", "modifiers referencing the node's own variables"),
        new("RF-7", "T-0083", "shadowing"),
        new("RF-8", "T-0083", "all children binding the same name"),
        new("RF-9", "T-0083", "references and ${} in literal fields"),
        new("RF-10", "T-0083", "forbidden and reserved environment names"),
        new("RF-15", "T-0083", "outputs bound only in a loop"),
        new("RF-16", "T-0083", "captures under finally"),
        new("RF-19", "T-0083", "secret literals"),
        new("RF-23", "T-0083", "references with no binding"),
        new("RF-26", "T-0083", "names bound twice"),
        new("RF-27", "T-0083", "templates outside set and vars"),
        new("BK_FROM_ENV_UNSET", "T-0083", "from_env in vars, resolved before the run"),

        // T-0084: conditions.
        new("CO-4", "T-0084", "condition shape: subjects, operators, a node as a condition"),
        new("CO-12", "T-0084", "ignore_case on other operators"),
        new("CO-26", "T-0084", "probe exec/script/shell"),
        new("CO-44", "T-0084", "service-only subjects"),
        new("CO-47", "T-0084", "operator and subject pairs"),
        new("RF-13", "T-0084", "the result subject"),
        new("RF-14", "T-0084", "not around a simple condition"),

        // T-0085: reference and modifiers.
        new("RF-2", "T-0085", "one repetition modifier"),
        new("RF-3", "T-0085", "the validity matrix and sibling table"),
        new("RF-4", "T-0085", "attempt_timeout with repetition"),
        new("RF-5", "T-0085", "bounded while and until"),
        new("RF-11", "T-0085", "absolute cwd with tempdir"),
        new("RF-12", "T-0085", "captures on leaves only"),
        new("RF-17", "T-0085", "lock inside lock"),
        new("RF-18", "T-0085", "for as"),
        new("RF-20", "T-0085", "input default and constraints"),
        new("RF-24", "T-0085", "service names"),
        new("RF-25", "T-0085", "for options with their source"),
        new("RF-28", "T-0085", "modifiers on no-process kinds"),
        new("RF-29", "T-0085", "return placement"),
        new("RF-30", "T-0085", "index, carry and collect"),
        new("RF-31", "T-0085", "concurrency and fail_fast with for"),
        new("RF-32", "T-0085", "companion fields"),
        new("RF-33", "T-0085", "ignore_errors on the root"),
        new("SH-17", "T-0085", "shell availability"),
        new("CP-19", "T-0085", "container rules"),
        new("containers/validation/mount-path-not-absolute", "T-0085", "container mount paths (no rule ID asserted)"),
    ];

    /// <summary>The entry an expected error of case <paramref name="caseId"/> falls under, or null.</summary>
    public static PendingEntry? For(string caseId, string? code, string? rule) =>
        Entries.FirstOrDefault(e => e.Key == rule || e.Key == code || e.Key == caseId);
}

/// <summary>
/// The later-feature validation pass (ADR 0009, validation-strategy.md §2): a validation case whose features aren't all
/// validated passes when <c>blokky validate</c> reports the case's expected errors, with <c>BK_PLAN_NOT_SUPPORTED</c>
/// allowed in addition. It reuses the not-supported check's invocation, which passes no inputs: inputs only matter once the
/// plan step passes, which unsupported features prevent.
/// </summary>
internal static class LaterFeaturePass
{
    public static LaterResult Judge(ConformanceCase c, AdapterOutcome outcome, int logCount, int survivors, Matcher matcher)
    {
        ArgumentNullException.ThrowIfNull(c);
        ArgumentNullException.ThrowIfNull(outcome);
        ArgumentNullException.ThrowIfNull(matcher);
        if (c.Expect.GetValueOrDefault("input_errors") is List<object?> { Count: > 0 })
        {
            return new(LaterKind.NotApplicable, "expects input errors, which are checked after the plan step", []);
        }

        var expected = (c.Expect.GetValueOrDefault("plan_errors") as List<object?> ?? [])
            .OfType<OrderedDictionary<string, object?>>().ToList();
        var keys = expected.Select(e => PendingRuleFamilies.For(c.Id, e.GetValueOrDefault("code") as string, RuleOf(e))?.Key)
            .OfType<string>().Distinct(StringComparer.Ordinal).ToList();
        var reported = outcome with
        {
            PlanErrors = [.. outcome.PlanErrors.Where(e => (e as OrderedDictionary<string, object?>)?.GetValueOrDefault("code") is not
                ErrorCodes.PlanNotSupported)],
        };
        var mismatch = CaseRunner.MatchValidation(c.Expect, reported, logCount, matcher, survivors);
        if (mismatch is null)
        {
            return new(LaterKind.Pass, null, keys);
        }

        if (keys.Count == 0)
        {
            return new(LaterKind.Fail, mismatch, keys);
        }

        // Pending: only expected errors of pending families may be missing; nothing else may be wrong.
        if (outcome.Kind != OutcomeKind.Rejected || outcome.Phase != "plan" || logCount > 0 || survivors > 0)
        {
            return new(LaterKind.Fail, mismatch, keys);
        }

        var required = expected.Where(e => PendingRuleFamilies.For(c.Id, e.GetValueOrDefault("code") as string, RuleOf(e)) is null)
            .Cast<object?>().ToList();
        if (required.Count > 0
            && matcher.Match(new OrderedDictionary<string, object?> { ["$contains"] = required }, reported.PlanErrors, "plan_errors")
                is { } missing)
        {
            return new(LaterKind.Fail, $"{missing} (the case's other errors are pending: {string.Join(", ", keys)})", keys);
        }

        if (c.Expect.GetValueOrDefault("exhaustive") is true)
        {
            foreach (var error in reported.PlanErrors)
            {
                if (!expected.Any(e => matcher.Match(e, error, "") is null))
                {
                    return new(LaterKind.Fail, $"exhaustive: an error the case doesn't expect: {YamlShow(error)}", keys);
                }
            }
        }

        return new(LaterKind.Pending, $"pending rule family: {string.Join(", ", keys.Select(Describe))}", keys);
    }

    /// <summary>Entries that no case needs any more: every case they apply to passes. Meaningful for a full run only.</summary>
    public static IReadOnlyList<PendingEntry> Stale(IEnumerable<LaterResult> results)
    {
        var list = results.ToList();
        var pending = list.Where(r => r.Kind == LaterKind.Pending).SelectMany(r => r.PendingKeys).ToHashSet(StringComparer.Ordinal);
        var passed = list.Where(r => r.Kind == LaterKind.Pass).SelectMany(r => r.PendingKeys).ToHashSet(StringComparer.Ordinal);
        return [.. PendingRuleFamilies.Entries.Where(e => passed.Contains(e.Key) && !pending.Contains(e.Key))];
    }

    private static string Describe(string key) =>
        PendingRuleFamilies.Entries.First(e => e.Key == key) is var e ? $"{e.Key} ({e.Task})" : key;

    private static string? RuleOf(OrderedDictionary<string, object?> error) =>
        (error.GetValueOrDefault("detail") as OrderedDictionary<string, object?>)?.GetValueOrDefault("rule") as string;

    private static string YamlShow(object? error) =>
        error is OrderedDictionary<string, object?> e
            ? $"{e.GetValueOrDefault("code")} {RuleOf(e)} at {e.GetValueOrDefault("line")}:{e.GetValueOrDefault("column")}"
            : Yaml.YamlValues.Show(error);
}
