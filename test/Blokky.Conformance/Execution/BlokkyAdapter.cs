using System.Text;
using System.Text.Json;
using Blokky.Conformance.Cases;
using Blokky.Core.Features;

namespace Blokky.Conformance.Execution;

/// <summary>What the adapter declares (CF-54).</summary>
/// <param name="Validated">Feature ids the validator fully checks: Core's validated table, declared for validation cases.</param>
/// <param name="Runnable">Feature ids the run engine executes: Core's runnable table, declared for run cases (ADR 0009).</param>
/// <param name="ClockModes">Clock modes the binary implements (§5.1).</param>
/// <param name="ExternalStop">Whether it can stop a run from outside (CF-55).</param>
/// <param name="Seed">Whether it takes an RNG seed (CF-41).</param>
internal sealed record AdapterCapabilities(
    IReadOnlySet<string> Validated,
    IReadOnlySet<string> Runnable,
    IReadOnlyList<string> ClockModes,
    bool ExternalStop,
    bool Seed)
{
    /// <summary>
    /// This build: Core's two feature tables; only <c>real</c>, since the clock can be selected only in test builds of
    /// blokky and none exists yet (testkit.md §5); external stops by SIGTERM on Unix.
    /// </summary>
    public static AdapterCapabilities Current { get; } =
        new(FeatureSupport.Validated, FeatureSupport.Runnable, ["real"], !OperatingSystem.IsWindows(), Seed: false);

    /// <summary>The supported features declared for a case (CF-12): validated for a validation outcome, else runnable.</summary>
    public IReadOnlySet<string> FeaturesFor(ExpectKind kind) => kind == ExpectKind.Validation ? Validated : Runnable;
}

/// <summary>How a run ended, as the adapter sees it.</summary>
internal enum OutcomeKind
{
    /// <summary>The pre-run envelope: plan, input or usage errors; nothing ran.</summary>
    Rejected,

    /// <summary><c>blokky validate</c> accepted the plan (<c>status: valid</c>); nothing ran.</summary>
    Valid,

    /// <summary>A run report, mapped to the result document.</summary>
    Result,

    /// <summary>The process was killed by a <c>kill</c> action (CF-65).</summary>
    NoResult,

    /// <summary>The harness deadline fired (CF-44).</summary>
    Deadline,

    /// <summary>The output isn't one JSON document of a known shape.</summary>
    Broken,
}

/// <summary>The adapter's answer for one run (CF-53).</summary>
internal sealed record AdapterOutcome(
    OutcomeKind Kind,
    int ExitCode,
    string? Phase,
    List<object?> PlanErrors,
    List<object?> InputErrors,
    List<object?> OtherErrors,
    OrderedDictionary<string, object?>? Result,
    string? Problem,
    string Stderr)
{
    public IEnumerable<object?> AllErrors => PlanErrors.Concat(InputErrors).Concat(OtherErrors);
}

/// <summary>The <c>blokky</c> command a request runs.</summary>
internal enum AdapterCommand
{
    /// <summary><c>blokky validate</c>: gates on the validated table.</summary>
    Validate,

    /// <summary><c>blokky run</c>: gates on the runnable table.</summary>
    Run,
}

/// <summary>A run request (CF-53).</summary>
/// <param name="Command"><see cref="BlokkyAdapter.CommandFor"/> the case's expected outcome.</param>
internal sealed record RunRequest(
    AdapterCommand Command,
    byte[] Plan,
    IReadOnlyList<string> Inputs,
    IReadOnlyDictionary<string, string> Env,
    string Cwd,
    TimeSpan Deadline,
    IReadOnlyList<TimedAction> Actions);

/// <summary>
/// The blokky-cli adapter (CF-2, validation-strategy.md §2): <c>blokky validate</c> for a validation outcome and
/// <c>blokky run</c> otherwise (ADR 0009), with the plan on stdin and each input as <c>--input</c>, then the pre-run
/// envelope or the run report from stdout, mapped to errors or the result document. Both commands write the same pre-run
/// envelope with the same exit codes (cli.md), so a case is judged against the table its command gates on, the one
/// <see cref="AdapterCapabilities.FeaturesFor"/> declares.
/// </summary>
internal sealed class BlokkyAdapter(string blokky, IProcessRunner runner, AdapterCapabilities? capabilities = null)
{
    public AdapterCapabilities Capabilities { get; } = capabilities ?? AdapterCapabilities.Current;

    public string Binary => blokky;

    /// <summary>
    /// The command for a case: <c>validate</c> for a validation outcome, whose features are judged against the validated
    /// table; <c>run</c> for the others.
    /// </summary>
    public static AdapterCommand CommandFor(ExpectKind kind) =>
        kind == ExpectKind.Validation ? AdapterCommand.Validate : AdapterCommand.Run;

    public static IReadOnlyList<string> Arguments(AdapterCommand command, IReadOnlyList<string> inputs)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        var args = new List<string> { command == AdapterCommand.Validate ? "validate" : "run" };
        foreach (var input in inputs)
        {
            // CF-18: split at the first '=' by blokky itself; a string without '=' is passed unsplit.
            args.Add("--input");
            args.Add(input);
        }

        args.Add("-");
        return args;
    }

    public async Task<AdapterOutcome> RunAsync(RunRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var spec = new ProcessSpec(blokky, Arguments(request.Command, request.Inputs), request.Env, request.Cwd, request.Plan,
            request.Deadline, request.Actions);
        var outcome = await runner.RunAsync(spec, cancellationToken).ConfigureAwait(false);
        return Interpret(outcome);
    }

    /// <summary>
    /// Reads stdout: one JSON document (OC-1), the envelope (<c>status: invalid</c>), the validate verdict
    /// (<c>status: valid</c>) or a report.
    /// </summary>
    internal static AdapterOutcome Interpret(ProcessOutcome outcome)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        var stderr = Encoding.UTF8.GetString(outcome.Stderr);
        AdapterOutcome Make(
            OutcomeKind kind, string? problem = null, string? phase = null, OrderedDictionary<string, object?>? result = null) =>
            new(kind, outcome.ExitCode, phase, [], [], [], result, problem, stderr);

        if (outcome.Killed)
        {
            return Make(OutcomeKind.NoResult);
        }

        if (outcome.DeadlineHit)
        {
            return Make(OutcomeKind.Deadline, "the harness deadline fired (CF-44)");
        }

        object? document;
        try
        {
            using var json = JsonDocument.Parse(outcome.Stdout);
            document = JsonValues.From(json.RootElement);
        }
        catch (JsonException ex)
        {
            var head = Encoding.UTF8.GetString(outcome.Stdout.AsSpan(0, Math.Min(outcome.Stdout.Length, 200)));
            return Make(OutcomeKind.Broken,
                $"stdout isn't one JSON document (exit {outcome.ExitCode}): {ex.Message}; stdout starts '{head}'; stderr: {Trim(stderr)}");
        }

        if (document is not OrderedDictionary<string, object?> root)
        {
            return Make(OutcomeKind.Broken, "stdout isn't a JSON object");
        }

        if (root.GetValueOrDefault("status") is "invalid")
        {
            var phase = root.GetValueOrDefault("phase") as string;
            var rejected = Make(OutcomeKind.Rejected, phase: phase);
            if (root.GetValueOrDefault("errors") is not List<object?> errors || errors.Count == 0)
            {
                return Make(OutcomeKind.Broken, "the pre-run envelope has no errors (OC-12)");
            }

            foreach (var error in errors)
            {
                var mapped = ReportMapper.Error(error);
                var code = (mapped as OrderedDictionary<string, object?>)?.GetValueOrDefault("code") as string ?? "";
                var bucket = phase switch
                {
                    "plan" => rejected.PlanErrors,
                    "usage" when code.StartsWith("BK_INPUT_", StringComparison.Ordinal) => rejected.InputErrors,
                    _ => rejected.OtherErrors,
                };
                bucket.Add(mapped);
            }

            return rejected;
        }

        if (root.GetValueOrDefault("status") is "valid")
        {
            return Make(OutcomeKind.Valid);
        }

        return Make(OutcomeKind.Result, result: ReportMapper.Map(root));
    }

    private static string Trim(string text) => text.Length <= 300 ? text.TrimEnd() : text[..300] + "…";
}
