using System.Diagnostics;
using System.Text;
using Blokky.Conformance.Cases;
using Blokky.Conformance.Matching;
using Blokky.Conformance.Yaml;
using Blokky.Core.Features;

namespace Blokky.Conformance.Execution;

/// <summary>A case's outcome (CF-62).</summary>
internal enum Outcome
{
    Pass,
    Fail,
    Skip,
    Error,
}

/// <summary>One line of the report (CF-63).</summary>
internal sealed record CaseResult(
    string Id,
    Outcome Outcome,
    string? Reason,
    SkipKind? SkipKind,
    IReadOnlyList<string> Rules,
    string? Clock,
    TimeSpan Elapsed,
    string? Note = null,
    LaterResult? Later = null);

/// <summary>Runner settings.</summary>
/// <param name="RunRoot">Where per-case directories are created.</param>
/// <param name="TimingFactor">Multiplies <c>$max</c> bounds and the harness deadline (CF-43, CF-44).</param>
/// <param name="Scale">The factor <c>k</c> of the scaled clock mode (CF-40).</param>
/// <param name="KeepWorkDirs">Keep each case's directories for debugging.</param>
internal sealed record RunnerSettings(string RunRoot, double TimingFactor, double Scale, bool KeepWorkDirs)
{
    /// <summary>The harness deadline in plan time (CF-44).</summary>
    public static readonly TimeSpan PlanDeadline = TimeSpan.FromSeconds(60);

    /// <summary>How long processes a run left behind may still act (log, write files) before the outcome is read.</summary>
    public static readonly TimeSpan BaseSettle = TimeSpan.FromMilliseconds(300);

    /// <summary>How long the cleanup keeps killing a case's processes before giving up.</summary>
    public static readonly TimeSpan KillLimit = TimeSpan.FromSeconds(5);

    /// <summary>The settle time in real time, stretched by the timing factor like the other bounds (CF-43).</summary>
    public TimeSpan Settle => BaseSettle * TimingFactor;
}

/// <summary>Prepares, runs and checks one case (CF-60, CF-61).</summary>
internal sealed class CaseRunner(BlokkyAdapter adapter, HostInfo host, RunnerSettings settings)
{
    private readonly Matcher _matcher = new(MatchOptions.Default with { TimingFactor = settings.TimingFactor });

    public async Task<CaseResult> RunAsync(string casesDir, string file, CancellationToken cancellationToken)
    {
        var id = CaseLoader.IdOf(casesDir, file);
        var clock = Stopwatch.StartNew();
        ConformanceCase c;
        try
        {
            c = CaseLoader.Load(casesDir, file, host.Os);
        }
        catch (CaseFormatException ex)
        {
            return new CaseResult(id, Outcome.Error, $"broken case: {ex.Message}", null, [], null, clock.Elapsed);
        }

        try
        {
            var selection = CaseSelector.Select(c, host, adapter.Capabilities);
            return selection.Skip switch
            {
                SkipKind.NotSupported => await NotSupportedCheckAsync(c, selection, clock, cancellationToken).ConfigureAwait(false),
                { } kind => new CaseResult(id, Outcome.Skip, selection.Reason, kind, c.Rules, null, clock.Elapsed),
                null => await RunCaseAsync(c, selection.Clock!, clock, cancellationToken).ConfigureAwait(false),
            };
        }
        catch (CaseFormatException ex)
        {
            return new CaseResult(id, Outcome.Error, $"broken case: {ex.Message}", null, c.Rules, null, clock.Elapsed);
        }
#pragma warning disable CA1031 // A runner failure is the case's `error` outcome (CF-62), never a crash of the whole run.
        catch (Exception ex) when (ex is not OperationCanceledException)
#pragma warning restore CA1031
        {
            return new CaseResult(id, Outcome.Error, $"runner failure: {ex.GetType().Name}: {ex.Message}", null, c.Rules, null,
                clock.Elapsed);
        }
    }

    /// <summary>
    /// CF-12: the plan is rejected in validation with <c>BK_PLAN_NOT_SUPPORTED</c> and no process starts. The check passes
    /// the plan without the case's inputs: inputs aren't features, and a malformed one is a usage error that blokky reports
    /// before it reads the plan (cli.md precedence). A validation case whose own expected plan errors are reported instead
    /// also passes (a plan that can't be parsed has no features to name), with the same exit code and phase.
    /// </summary>
    private async Task<CaseResult> NotSupportedCheckAsync(ConformanceCase c, Selection selection, Stopwatch clock, CancellationToken ct)
    {
        using var ws = CaseWorkspace.Create(settings.RunRoot, c.Id, settings.KeepWorkDirs);
        try
        {
            ws.Populate(c, host.Testkit);
            var request = new RunRequest(BlokkyAdapter.CommandFor(c.ExpectKind), c.Plan, [], ws.Environment(c, host, 1.0), ws.WorkDir,
                Deadline(1), []);
            var outcome = await adapter.RunAsync(request, ct).ConfigureAwait(false);
            var survivors = (await Survivors.SettleAsync(ws.Marker, settings.Settle, ct).ConfigureAwait(false)).Count;
            var logCount = ws.LogCount();
            var (problem, note) = NotSupportedProblem(outcome, logCount, c.Features, adapter.Capabilities.FeaturesFor(c.ExpectKind),
                survivors);
            if (problem is not null && RejectedWithOwnErrors(c.ExpectKind, c.Expect, outcome, logCount, survivors, _matcher))
            {
                (problem, note) = (null, "rejected with the case's own validation errors instead of BK_PLAN_NOT_SUPPORTED");
            }

            // ADR 0009: the later-feature validation pass judges the same invocation against the case's expected errors, and a
            // valid plan (run and no-result cases too) against BK_PLAN_NOT_SUPPORTED alone.
            var later = LaterFeaturePass.Judge(c, outcome, logCount, survivors, _matcher);
            return problem is null
                ? new CaseResult(c.Id, Outcome.Skip, selection.Reason, SkipKind.NotSupported, c.Rules, null, clock.Elapsed, note, later)
                : new CaseResult(c.Id, Outcome.Fail, $"not-supported check (CF-12): {problem}", SkipKind.NotSupported, c.Rules, null,
                    clock.Elapsed, note, later);
        }
        finally
        {
            await Survivors.KillAsync(ws.Marker, RunnerSettings.KillLimit).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// The not-supported check's fallback: a validation case whose plan doesn't parse is rejected with its own expected plan
    /// errors, as a plan rejection (exit 3, phase <c>plan</c>, like <see cref="NotSupportedProblem"/> asks) with no process.
    /// Cases never state exit codes (CF-1); plan errors are phase <c>plan</c> and exit 3 (cli.md precedence, step 4). A case
    /// that expects only input errors can't take it: the check passes no inputs.
    /// </summary>
    internal static bool RejectedWithOwnErrors(ExpectKind kind, OrderedDictionary<string, object?> expect, AdapterOutcome outcome,
        int logCount, int survivors, Matcher matcher)
    {
        ArgumentNullException.ThrowIfNull(expect);
        ArgumentNullException.ThrowIfNull(outcome);
        return kind == ExpectKind.Validation
            && expect.GetValueOrDefault("plan_errors") is List<object?> { Count: > 0 }
            && outcome.ExitCode == 3
            && outcome.Phase == "plan"
            && MatchValidation(expect, outcome, logCount, matcher, survivors) is null;
    }

    /// <summary>Why the not-supported check fails, or null; and a note on a <c>detail.feature</c> the case doesn't list.</summary>
    /// <param name="logCount">Testkit invocations in the case's log.</param>
    /// <param name="declared">The features the adapter declares for this case (validated or runnable, ADR 0009).</param>
    /// <param name="survivors">The case's processes still alive after <c>blokky</c> exited and the settle time.</param>
    internal static (string? Problem, string? Note) NotSupportedProblem(AdapterOutcome outcome, int logCount,
        IReadOnlyList<string> caseFeatures, IReadOnlySet<string> declared, int survivors = 0)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        ArgumentNullException.ThrowIfNull(caseFeatures);
        ArgumentNullException.ThrowIfNull(declared);
        if (logCount > 0)
        {
            return ($"a process started: {logCount} testkit invocation(s) (EM-1)", null);
        }

        if (survivors > 0)
        {
            return ($"a process started: {survivors} process(es) of the case outlived blokky (EM-1)", null);
        }

        if (outcome.Kind != OutcomeKind.Rejected)
        {
            return ($"expected a rejection in validation, got {outcome.Kind}{(outcome.Problem is null ? "" : ": " + outcome.Problem)}",
                null);
        }

        if (outcome.ExitCode != 3 || outcome.Phase != "plan")
        {
            return ($"expected exit 3 in phase plan, got exit {outcome.ExitCode} in phase {outcome.Phase ?? "(none)"}: {Codes(outcome)}",
                null);
        }

        var notSupported = outcome.PlanErrors.OfType<OrderedDictionary<string, object?>>()
            .FirstOrDefault(e => e.GetValueOrDefault("code") is "BK_PLAN_NOT_SUPPORTED");
        if (notSupported is null)
        {
            return ($"no BK_PLAN_NOT_SUPPORTED among {Codes(outcome)}", null);
        }

        var feature = (notSupported.GetValueOrDefault("detail") as OrderedDictionary<string,
            object?>)?.GetValueOrDefault("feature") as string;
        if (feature is null || !FeatureIds.IsKnown(feature))
        {
            return ($"BK_PLAN_NOT_SUPPORTED's detail.feature is {YamlValues.Show(feature)}, not a feature id (errors.md)", null);
        }

        if (declared.Contains(feature))
        {
            return ($"BK_PLAN_NOT_SUPPORTED names '{feature}', which this build declares supported", null);
        }

        return (null, caseFeatures.Contains(feature) ? null : $"detail.feature '{feature}' isn't among the case's features");
    }

    private async Task<CaseResult> RunCaseAsync(ConformanceCase c, string clockMode, Stopwatch clock, CancellationToken ct)
    {
        var k = clockMode == "scaled" ? settings.Scale : 1.0;
        using var ws = CaseWorkspace.Create(settings.RunRoot, c.Id, settings.KeepWorkDirs);
        try
        {
            ws.Populate(c, host.Testkit);
            var env = ws.Environment(c, host, 1.0 / k);
            var actions = c.Actions.Select(a => new TimedAction(a.After / k, a.Kill)).ToList();
            var request = new RunRequest(BlokkyAdapter.CommandFor(c.ExpectKind), c.Plan, c.Inputs, env, ws.WorkDir, Deadline(k),
                actions);
            var outcome = await adapter.RunAsync(request, ct).ConfigureAwait(false);

            // Processes the run left behind (expected ones too: after_run checks them) get the settle time to log.
            var survivors = (await Survivors.SettleAsync(ws.Marker, settings.Settle, ct).ConfigureAwait(false)).Count;
            var mismatch = outcome.Kind == OutcomeKind.Deadline
                ? "the harness deadline fired (CF-44)"
                : c.ExpectKind switch
                {
                    ExpectKind.Validation => MatchValidation(c.Expect, outcome, ws.LogCount(), _matcher, survivors),
                    ExpectKind.Run => MatchRun(c.Expect, outcome, ws.ReadLog(), ws.WorkDir, _matcher),
                    _ => MatchNoResult(c.Expect, outcome, ws.ReadLog(), ws.WorkDir, _matcher),
                };

            for (var i = 0; mismatch is null && i < c.AfterRun.Count; i++)
            {
                mismatch = await AfterRunAsync(c.AfterRun[i], ws, env, k, ct).ConfigureAwait(false) is { } m
                    ? $"after_run[{i}]: {m}"
                    : null;
            }

            return mismatch is null
                ? new CaseResult(c.Id, Outcome.Pass, null, null, c.Rules, clockMode, clock.Elapsed)
                : new CaseResult(c.Id, Outcome.Fail, mismatch, null, c.Rules, clockMode, clock.Elapsed);
        }
        finally
        {
            // After the checks (survivors may be expected until then), before the workspace goes and the port is released.
            await Survivors.KillAsync(ws.Marker, RunnerSettings.KillLimit).ConfigureAwait(false);
        }
    }

    private async Task<string?> AfterRunAsync(AfterRunCheck check, CaseWorkspace ws, Dictionary<string, string> env, double k,
        CancellationToken ct)
    {
        if (check.Argv is { } argv)
        {
            var spec = new ProcessSpec(ws.Testkit!, argv, env, ws.WorkDir, [], Deadline(k), []);
            var result = await new ProcessRunner().RunAsync(spec, ct).ConfigureAwait(false);
            return Matcher.ScalarEquals(check.ExitCode, (long)result.ExitCode)
                ? null
                : $"{string.Join(' ', argv)}: expected exit {YamlValues.Show(check.ExitCode)}, got {result.ExitCode}: "
                    + Encoding.UTF8.GetString(result.Stdout).TrimEnd();
        }

        var before = ws.LogCount();
        var request = new RunRequest(BlokkyAdapter.CommandFor(check.ExpectKind), check.Plan!, check.Inputs, env, ws.WorkDir,
            Deadline(k), []);
        var outcome = await adapter.RunAsync(request, ct).ConfigureAwait(false);
        await Survivors.SettleAsync(ws.Marker, settings.Settle, ct).ConfigureAwait(false);
        return check.ExpectKind == ExpectKind.Validation
            ? MatchValidation(check.Expect!, outcome, ws.LogCount() - before, _matcher)
            : MatchRun(check.Expect!, outcome, ws.ReadLog(before), ws.WorkDir, _matcher);
    }

    /// <summary>A validation outcome (CF-23, CF-61): the reported errors, and no process started.</summary>
    /// <param name="survivors">The case's processes still alive after <c>blokky</c> exited and the settle time.</param>
    internal static string? MatchValidation(OrderedDictionary<string, object?> expect, AdapterOutcome outcome, int logCount,
        Matcher matcher, int survivors = 0)
    {
        ArgumentNullException.ThrowIfNull(expect);
        ArgumentNullException.ThrowIfNull(outcome);
        ArgumentNullException.ThrowIfNull(matcher);
        if (outcome.Kind != OutcomeKind.Rejected)
        {
            return $"expected plan or input errors, got {outcome.Kind}{(outcome.Problem is null ? "" : ": " + outcome.Problem)}";
        }

        if (logCount > 0)
        {
            return $"a process started during validation: {logCount} testkit invocation(s) (CF-23)";
        }

        if (survivors > 0)
        {
            return $"a process started during validation: {survivors} process(es) of the case outlived blokky (CF-23)";
        }

        if (outcome.OtherErrors.Count > 0)
        {
            return $"the invocation was rejected in phase {outcome.Phase}: {Codes(outcome)}";
        }

        foreach (var (key, actual) in new[] { ("plan_errors", outcome.PlanErrors), ("input_errors", outcome.InputErrors) })
        {
            if (expect.TryGetValue(key, out var wanted) && wanted is not null
                && matcher.Match(new OrderedDictionary<string, object?> { ["$contains"] = wanted }, actual, key) is { } mismatch)
            {
                return mismatch;
            }
        }

        if (expect.GetValueOrDefault("exhaustive") is true)
        {
            var listed = (expect.GetValueOrDefault("plan_errors") as List<object?>)?.Count ?? 0;
            listed += (expect.GetValueOrDefault("input_errors") as List<object?>)?.Count ?? 0;
            var reported = outcome.PlanErrors.Count + outcome.InputErrors.Count;
            if (listed != reported)
            {
                return $"exhaustive: expected {listed} error(s), got {reported}: {Codes(outcome)}";
            }
        }

        return null;
    }

    /// <summary>A run outcome (CF-61): a result document, matched key by key, plus the invocation log and the files.</summary>
    internal static string? MatchRun(
        OrderedDictionary<string, object?> expect, AdapterOutcome outcome, List<object?> invocations, string workDir, Matcher matcher)
    {
        ArgumentNullException.ThrowIfNull(expect);
        ArgumentNullException.ThrowIfNull(outcome);
        ArgumentNullException.ThrowIfNull(matcher);
        if (outcome.Kind != OutcomeKind.Result || outcome.Result is null)
        {
            return $"expected a run result (EM-3), got {outcome.Kind}: {outcome.Problem ?? Codes(outcome)}";
        }

        var actual = new OrderedDictionary<string, object?>(outcome.Result, StringComparer.Ordinal) { ["invocations"] = invocations };
        foreach (var (key, wanted) in expect)
        {
            var mismatch = key == "files"
                ? MatchFiles(wanted, workDir, matcher)
                : matcher.Match(wanted, actual.TryGetValue(key, out var value) ? value : Missing.Value, key);
            if (mismatch is not null)
            {
                return mismatch;
            }
        }

        return null;
    }

    /// <summary>No result (CF-65): the implementation was killed; only <c>invocations</c> and <c>files</c> are checked.</summary>
    internal static string? MatchNoResult(
        OrderedDictionary<string, object?> expect, AdapterOutcome outcome, List<object?> invocations, string workDir, Matcher matcher)
    {
        ArgumentNullException.ThrowIfNull(expect);
        ArgumentNullException.ThrowIfNull(outcome);
        ArgumentNullException.ThrowIfNull(matcher);
        if (outcome.Kind != OutcomeKind.NoResult)
        {
            return $"expected the kill action to end the run, got {outcome.Kind}";
        }

        if (expect.TryGetValue("invocations", out var wanted) && matcher.Match(wanted, invocations, "invocations") is { } mismatch)
        {
            return mismatch;
        }

        return expect.TryGetValue("files", out var files) ? MatchFiles(files, workDir, matcher) : null;
    }

    /// <summary><c>files</c>: <c>{ exists }</c>, <c>text</c>, <c>base64</c> or <c>contains</c> per path (§3.5).</summary>
    internal static string? MatchFiles(object? expected, string workDir, Matcher matcher)
    {
        ArgumentNullException.ThrowIfNull(matcher);
        if (expected is not OrderedDictionary<string, object?> files)
        {
            throw new CaseFormatException("expect.files is a mapping");
        }

        foreach (var (relative, spec) in files)
        {
            var path = $"files.{relative}";
            var full = Path.Combine(workDir, relative.Replace('/', Path.DirectorySeparatorChar));
            var exists = File.Exists(full) || Directory.Exists(full);
            var entry = spec switch
            {
                string plain => new OrderedDictionary<string, object?> { ["text"] = plain },
                OrderedDictionary<string, object?> m => m,
                _ => throw new CaseFormatException($"expect.{path} is a string or a mapping"),
            };
            if (entry.Keys.FirstOrDefault(k => k is not ("exists" or "text" or "base64" or "contains")) is { } bad)
            {
                throw new CaseFormatException($"expect.{path}: unknown key '{bad}'");
            }

            if (entry.TryGetValue("exists", out var shouldExist) && shouldExist is bool want && want != exists)
            {
                return $"{path}: expected the file {(want ? "to exist" : "not to exist")}";
            }

            if (!entry.Keys.Any(k => k is "text" or "base64" or "contains"))
            {
                continue;
            }

            if (!File.Exists(full))
            {
                return $"{path}: expected a file, got none";
            }

            var bytes = File.ReadAllBytes(full);
            string? text;
            try
            {
                text = new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(bytes);
            }
            catch (DecoderFallbackException)
            {
                text = null;
            }

            if (entry.TryGetValue("text", out var wantText) && matcher.Match(wantText, (object?)text ?? Missing.Value,
                path + ".text") is { } m1)
            {
                return m1;
            }

            if (entry.TryGetValue("base64", out var wantBase64) && matcher.Match(wantBase64, Convert.ToBase64String(bytes),
                path + ".base64") is { } m2)
            {
                return m2;
            }

            if (entry.TryGetValue("contains", out var part)
                && (part is not string s || text is null || !text.Contains(s, StringComparison.Ordinal)))
            {
                return $"{path}: expected text containing {YamlValues.Show(part)}, got {YamlValues.Show(text)}";
            }
        }

        return null;
    }

    private TimeSpan Deadline(double k) => RunnerSettings.PlanDeadline * settings.TimingFactor / k;

    private static string Codes(AdapterOutcome outcome) =>
        "[" + string.Join(", ", outcome.AllErrors.OfType<OrderedDictionary<string, object?>>().Select(e =>
            e.GetValueOrDefault("code"))) + "]";
}
