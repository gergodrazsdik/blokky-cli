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
/// <param name="MissingKeys">
/// The entries of <paramref name="PendingKeys"/> with an expected error of their own that wasn't reported: the entries
/// this case still needs. An entry that applies to a case but isn't missing there is stale for that case.
/// </param>
internal sealed record LaterResult(LaterKind Kind, string? Reason, IReadOnlyList<string> PendingKeys, IReadOnlyList<string> MissingKeys);

/// <summary>An entry of the pending list: the rules a later task implements.</summary>
/// <param name="Key">A rule ID (<c>RF-3</c>), an error code (<c>BK_FROM_ENV_UNSET</c>) or a case id.</param>
/// <param name="Task">The task that implements it (T-0082 to T-0086).</param>
/// <param name="Why">What is missing.</param>
internal sealed record PendingEntry(string Key, string Task, string Why);

/// <summary>
/// The rule families not implemented yet, as an explicit, reviewed list: an expected error whose rule (or code, or case)
/// is listed may be missing from <c>blokky validate</c>'s output without failing the later-feature pass. Each of T-0082
/// to T-0086 deletes its entries; an entry none of whose cases misses an error of its own is reported as stale and fails
/// the run, so the list only shrinks. It is not an allow-list of failures: a pending case still fails when an expected
/// error of an implemented rule is missing, or (for an exhaustive case) when an error is reported that the case doesn't
/// expect.
/// </summary>
internal static class PendingRuleFamilies
{
    public static IReadOnlyList<PendingEntry> Entries { get; } =
    [

        // T-0084: conditions.

        // T-0085: reference and modifiers. RF-20's check of a default against matches: needs the portable regex subset.
        new("syntax/inputs/default-fails-matches", "T-0082", "RF-20: an input default checked against matches: (the regex subset)"),
    ];

    /// <summary>The entry an expected error of case <paramref name="caseId"/> falls under, or null.</summary>
    public static PendingEntry? For(string caseId, string? code, string? rule) =>
        Entries.FirstOrDefault(e => e.Key == rule || e.Key == code || e.Key == caseId);
}

/// <summary>
/// The later-feature validation pass (ADR 0009, validation-strategy.md §2): a validation case whose features aren't all
/// validated passes when <c>blokky validate</c> reports the case's expected errors, with <c>BK_PLAN_NOT_SUPPORTED</c>
/// allowed in addition. It reuses the not-supported check's invocation, which passes no inputs: inputs only matter once the
/// plan step passes, which unsupported features prevent. A case whose plan is valid (a run or no-result case, or a
/// validation case that expects only input errors) is judged too: any plan error but <c>BK_PLAN_NOT_SUPPORTED</c> fails it.
/// </summary>
internal static class LaterFeaturePass
{
    public static LaterResult Judge(ConformanceCase c, AdapterOutcome outcome, int logCount, int survivors, Matcher matcher)
    {
        ArgumentNullException.ThrowIfNull(c);
        ArgumentNullException.ThrowIfNull(outcome);
        ArgumentNullException.ThrowIfNull(matcher);
        var reportedErrors = outcome.PlanErrors.Where(e => !IsNotSupported(e)).ToList();
        var expectsPlanErrors = c.ExpectKind == ExpectKind.Validation
            && c.Expect.GetValueOrDefault("plan_errors") is List<object?> { Count: > 0 };
        if (!expectsPlanErrors)
        {
            // The plan is valid: only the feature gate may reject it (a stray error would hide behind the not-supported skip).
            if (reportedErrors.Count > 0)
            {
                return new(LaterKind.Fail,
                    $"the case's plan is valid, but blokky reported: {string.Join(", ", reportedErrors.Select(YamlShow))}", [], []);
            }

            return c.ExpectKind == ExpectKind.Validation
                ? new(LaterKind.NotApplicable, "expects input errors, which are checked after the plan step", [], [])
                : new(LaterKind.Pass, null, [], []);
        }

        if (c.Expect.GetValueOrDefault("input_errors") is List<object?> { Count: > 0 })
        {
            return new(LaterKind.NotApplicable, "expects input errors, which are checked after the plan step", [], []);
        }

        var expected = (c.Expect.GetValueOrDefault("plan_errors") as List<object?> ?? [])
            .OfType<OrderedDictionary<string, object?>>().ToList();
        var entries = expected.Select(e => PendingRuleFamilies.For(c.Id, e.GetValueOrDefault("code") as string, RuleOf(e))).ToList();
        var keys = entries.OfType<PendingEntry>().Select(e => e.Key).Distinct(StringComparer.Ordinal).ToList();

        // Which expected errors were reported, one to one: the implemented rules' errors take their matches first, so a
        // pending error counts as found only with a reported error that is left over.
        var fits = new bool[expected.Count, reportedErrors.Count];
        for (var i = 0; i < expected.Count; i++)
        {
            for (var j = 0; j < reportedErrors.Count; j++)
            {
                fits[i, j] = matcher.Match(expected[i], reportedErrors[j], "") is null;
            }
        }

        var indexes = Enumerable.Range(0, expected.Count).ToList();
        var found = Matcher.Assign(fits, [.. indexes.Where(i => entries[i] is null), .. indexes.Where(i => entries[i] is not null)]);
        var missingKeys = indexes.Where(i => !found[i] && entries[i] is not null).Select(i => entries[i]!.Key)
            .Distinct(StringComparer.Ordinal).ToList();

        var reported = outcome with { PlanErrors = reportedErrors };
        var mismatch = CaseRunner.MatchValidation(c.Expect, reported, logCount, matcher, survivors);
        if (mismatch is null)
        {
            return new(LaterKind.Pass, null, keys, missingKeys);
        }

        // Pending: only expected errors of pending families may be missing; nothing else may be wrong.
        if (missingKeys.Count == 0 || outcome.Kind != OutcomeKind.Rejected || outcome.Phase != "plan" || logCount > 0 || survivors > 0
            || outcome.OtherErrors.Count > 0)
        {
            return new(LaterKind.Fail, mismatch, keys, missingKeys);
        }

        var required = expected.Where((_, i) => entries[i] is null).Cast<object?>().ToList();
        if (required.Count > 0
            && matcher.Match(new OrderedDictionary<string, object?> { ["$contains"] = required }, reported.PlanErrors, "plan_errors")
                is { } missing)
        {
            return new(LaterKind.Fail, $"{missing} (the case's other errors are pending: {string.Join(", ", missingKeys)})", keys,
                missingKeys);
        }

        if (c.Expect.GetValueOrDefault("exhaustive") is true)
        {
            // As in CaseRunner.MatchValidation: every reported error needs an expected error of its own.
            var back = new bool[reportedErrors.Count, expected.Count];
            for (var j = 0; j < reportedErrors.Count; j++)
            {
                for (var i = 0; i < expected.Count; i++)
                {
                    back[j, i] = fits[i, j];
                }
            }

            var owned = Matcher.Assign(back, Enumerable.Range(0, reportedErrors.Count));
            if (Array.IndexOf(owned, false) is var extra and >= 0)
            {
                return new(LaterKind.Fail, $"exhaustive: an error the case doesn't expect (or more of it than expected): "
                    + YamlShow(reportedErrors[extra]), keys, missingKeys);
            }
        }

        return new(LaterKind.Pending, $"pending rule family: {string.Join(", ", missingKeys.Select(Describe))}", keys, missingKeys);
    }

    /// <summary>
    /// Entries that no case needs any more: they apply to some judged case, and in none of them is one of the entry's own
    /// expected errors missing. Meaningful for a full run only.
    /// </summary>
    public static IReadOnlyList<PendingEntry> Stale(IEnumerable<LaterResult> results)
    {
        var list = results.ToList();
        var applies = list.SelectMany(r => r.PendingKeys).ToHashSet(StringComparer.Ordinal);
        var needed = list.SelectMany(r => r.MissingKeys).ToHashSet(StringComparer.Ordinal);
        return [.. PendingRuleFamilies.Entries.Where(e => applies.Contains(e.Key) && !needed.Contains(e.Key))];
    }

    private static bool IsNotSupported(object? error) =>
        (error as OrderedDictionary<string, object?>)?.GetValueOrDefault("code") is ErrorCodes.PlanNotSupported;

    private static string Describe(string key) =>
        PendingRuleFamilies.Entries.First(e => e.Key == key) is var e ? $"{e.Key} ({e.Task})" : key;

    private static string? RuleOf(OrderedDictionary<string, object?> error) =>
        (error.GetValueOrDefault("detail") as OrderedDictionary<string, object?>)?.GetValueOrDefault("rule") as string;

    private static string YamlShow(object? error) =>
        error is OrderedDictionary<string, object?> e
            ? $"{e.GetValueOrDefault("code")} {RuleOf(e)} at {e.GetValueOrDefault("line")}:{e.GetValueOrDefault("column")}"
            : Yaml.YamlValues.Show(error);
}
