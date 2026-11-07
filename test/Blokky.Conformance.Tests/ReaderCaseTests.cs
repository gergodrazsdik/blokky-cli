using Blokky.Conformance.Cases;
using Blokky.Conformance.Execution;
using Blokky.Conformance.Matching;
using Blokky.Conformance.Yaml;
using Blokky.Core.Contracts;
using Blokky.Core.Errors;
using Blokky.Core.Reading;

namespace Blokky.Conformance.Tests;

/// <summary>
/// The plan reader against the blokky-specs suite, in-process: validation cases whose expected errors come from the
/// reader (encoding, characters, caps, YAML, duplicate keys, a root that isn't a mapping) must be matched exactly as the
/// conformance runner matches them; every other case's plan must read cleanly, or the reader would hide the errors the
/// later layers report. The checkout comes from <c>BLOKKY_SPECS</c>, else a <c>blokky-specs</c> sibling of this repo or
/// of one of its parents (a worktree); without one the tests skip.
/// </summary>
public class ReaderCaseTests
{
    private const string NoSpecs = "(no blokky-specs checkout)";

    private static readonly Matcher Matcher = new();

    private static readonly HashSet<string> ReaderCodes = new(StringComparer.Ordinal)
    {
        ErrorCodes.PlanEncoding, ErrorCodes.PlanCharacter, ErrorCodes.PlanTooLarge, ErrorCodes.PlanYaml, ErrorCodes.PlanDuplicateKey,
    };

    /// <summary>Validation cases with at least one reader-level expected error.</summary>
    public static IEnumerable<string> ReaderLevelCases() => CaseIds(c => IsReaderLevel(c));

    /// <summary>Every other case: its plan must read cleanly.</summary>
    public static IEnumerable<string> OtherCases() => CaseIds(c => !IsReaderLevel(c));

    [Test]
    [MethodDataSource(nameof(ReaderLevelCases))]
    public async Task Reader_reports_the_expected_errors(string id)
    {
        var c = LoadOrSkip(id);
        var outcome = Interpret(new PlanReader().Read(c.Plan).Errors);

        await Assert.That(CaseRunner.MatchValidation(c.Expect, outcome, 0, Matcher)).IsNull();
    }

    [Test]
    [MethodDataSource(nameof(OtherCases))]
    public async Task Other_plans_read_cleanly(string id)
    {
        var c = LoadOrSkip(id);
        var errors = new PlanReader().Read(c.Plan).Errors;

        await Assert.That(string.Join("; ", errors.Select(e => $"{e.Code} at {e.Pos}: {e.Message}"))).IsEqualTo("");
    }

    /// <summary>
    /// A fixed-seed mutation fuzz over the suite's plans (spike 3), plus generated stress plans: edits with YAML punctuation
    /// must never make the reader throw or hang (each plan has a timeout), and every error it reports carries a code and a
    /// position inside the text.
    /// </summary>
    [Test]
    public async Task Mutated_and_stress_plans_never_crash_or_hang_the_reader()
    {
        if (CasesDir() is not { } dir)
        {
            Skip.Test("no blokky-specs checkout: set BLOKKY_SPECS or check it out next to blokky-cli");
            return;
        }

        var plans = CaseLoader.FindCaseFiles(dir).Select(f => TryLoad(dir, f)).OfType<ConformanceCase>().Select(c => c.Plan).ToList();
        var random = new Random(20261107);
        byte[] punctuation = "[]{}:,-?&*!|>'\"#%@`\t\r\n \\"u8.ToArray();
        var problems = new List<string>();
        var clock = System.Diagnostics.Stopwatch.StartNew();
        for (var i = 0; i < 4000; i++)
        {
            var plan = plans[random.Next(plans.Count)].ToList();
            for (var edits = random.Next(1, 4); edits > 0 && plan.Count > 0; edits--)
            {
                var at = random.Next(plan.Count);
                var b = punctuation[random.Next(punctuation.Length)];
                switch (random.Next(3))
                {
                    case 0:
                        plan[at] = b;
                        break;
                    case 1:
                        plan.Insert(at, b);
                        break;
                    default:
                        plan.RemoveAt(at);
                        break;
                }
            }

            await CheckReadAsync($"#{i}", [.. plan], TimeSpan.FromSeconds(10), problems);
        }

        foreach (var (name, plan) in StressPlans())
        {
            await CheckReadAsync(name, plan, TimeSpan.FromSeconds(30), problems);
        }

        await Assert.That(string.Join("\n", problems.Take(5))).IsEqualTo("");
        await Assert.That(clock.Elapsed).IsLessThan(TimeSpan.FromSeconds(120));
    }

    /// <summary>Large plans, near the 4 MiB cap, of the shapes that once took quadratic time or memory.</summary>
    private static IEnumerable<(string Name, byte[] Plan)> StressPlans()
    {
        const int Size = 1024 * 1024;
        static byte[] Utf8(string text) => System.Text.Encoding.UTF8.GetBytes(text);
        yield return ("long flow line", Utf8("blokky: 1.0\nlog: x\nvars: {" + string.Join(", ", Enumerable.Range(0, Size / 10)
            .Select(i => $"K{i}: v")) + "}\n"));
        yield return ("deep [", Utf8(new string('[', Size)));
        yield return ("deep [ on lines", Utf8("a:\n" + string.Concat(Enumerable.Repeat(" [\n", Size / 3))));
        yield return ("many rejected characters", Utf8("log: '" + new string('\u0001', Size / 2) + "\u202E'\n# \u200B\n"));
        yield return ("long plain key", Utf8(new string('k', Size) + ": v\n"));
        yield return ("long plain key on line 2", Utf8("blokky: 1.0\n" + new string('k', 2000) + "\U0001F600: v\n"));
    }

    /// <summary>Reads <paramref name="plan"/> within <paramref name="timeout"/>; records a throw, a hang or a bad error.</summary>
    private static async Task CheckReadAsync(string name, byte[] plan, TimeSpan timeout, List<string> problems)
    {
        try
        {
            var result = await Task.Run(() => new PlanReader().Read(plan)).WaitAsync(timeout);
            foreach (var error in result.Errors)
            {
                if (OutsideText(plan, result, error) is { } problem)
                {
                    problems.Add($"{name}: {error.Code} at {error.Pos ?? "(none)"}: {problem}");
                    break;
                }
            }
        }
        catch (TimeoutException)
        {
            problems.Add($"{name}: no result within {timeout.TotalSeconds:0}s");
        }
#pragma warning disable CA1031 // Any exception is the finding.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            problems.Add($"{name}: {ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>
    /// Why <paramref name="error"/>'s position isn't in the text, or null: a code, a <c>line:column</c> with the line at most
    /// the line count and the column at most the line's length in code points plus one (its end).
    /// </summary>
    private static string? OutsideText(byte[] plan, PlanReadResult result, BlokkyError error)
    {
        if (error.Code.Length == 0 || error.Pos is not { } pos)
        {
            return "no code or position";
        }

        var parts = pos.Split(':');
        if (parts.Length != 2 || !int.TryParse(parts[0], out var line) || !int.TryParse(parts[1], out var column)
            || line < 1 || column < 1)
        {
            return "not line:column";
        }

        // Undecodable text (an encoding error) is checked on its lossy decoding, which agrees up to the first bad byte.
        var text = result.Text ?? System.Text.Encoding.UTF8.GetString(plan.AsSpan(plan.AsSpan().StartsWith("\uFEFF"u8) ? 3 : 0));
        var lines = result.Lines ?? new LineMap(text);
        if (line > lines.LineCount)
        {
            return $"line {line} of {lines.LineCount}";
        }

        var start = lines.LineStart(line);
        var end = line < lines.LineCount ? lines.LineStart(line + 1) : text.Length;
        while (end > start && text[end - 1] is ('\n' or '\r'))
        {
            end--;
        }

        var length = lines.At(end).Column - 1;
        return column <= length + 1 ? null : $"column {column} on a line of {length}";
    }

    /// <summary>The reader's errors as the adapter sees them in a pre-run envelope (exit 3, phase plan).</summary>
    private static AdapterOutcome Interpret(IReadOnlyList<BlokkyError> errors) =>
        errors.Count == 0
            ? BlokkyAdapter.Interpret(AdapterTests.Exited(0, """{"status":"succeeded","nodes":[]}"""))
            : BlokkyAdapter.Interpret(new ProcessOutcome(3, ContractWriter.ToUtf8(new PreRunEnvelope(Phase.Plan, errors)), [], false,
                false, TimeSpan.Zero));

    private static bool IsReaderLevel(ConformanceCase c) =>
        c.ExpectKind == ExpectKind.Validation
        && c.Expect.GetValueOrDefault("plan_errors") is List<object?> errors
        && errors.OfType<OrderedDictionary<string, object?>>().Any(IsReaderError);

    private static bool IsReaderError(OrderedDictionary<string, object?> error) =>
        error.GetValueOrDefault("code") is string code
        && (ReaderCodes.Contains(code)
            || (code == ErrorCodes.PlanValue
                && (error.GetValueOrDefault("detail") as OrderedDictionary<string, object?>)?.GetValueOrDefault("rule") is "SY-32"));

    private static ConformanceCase LoadOrSkip(string id)
    {
        if (id == NoSpecs || CasesDir() is not { } dir)
        {
            Skip.Test("no blokky-specs checkout: set BLOKKY_SPECS or check it out next to blokky-cli");
            throw new InvalidOperationException("unreachable");
        }

        return CaseLoader.Load(dir, Path.Combine(dir, id + ".yaml"), "linux");
    }

    private static List<string> CaseIds(Func<ConformanceCase, bool> select)
    {
        if (CasesDir() is not { } dir)
        {
            return [NoSpecs];
        }

        var ids = CaseLoader.FindCaseFiles(dir).Select(f => TryLoad(dir, f)).OfType<ConformanceCase>().Where(select).Select(c => c.Id)
            .ToList();
        return ids.Count == 0 ? [NoSpecs] : ids;
    }

    private static ConformanceCase? TryLoad(string dir, string file)
    {
        try
        {
            return CaseLoader.Load(dir, file, "linux");
        }
        catch (CaseFormatException)
        {
            return null; // the conformance runner reports broken cases
        }
    }

    /// <summary><c>BLOKKY_SPECS</c>, else the first <c>blokky-specs</c> sibling of the directories above the test binary.</summary>
    internal static string? CasesDir()
    {
        if (Environment.GetEnvironmentVariable("BLOKKY_SPECS") is { Length: > 0 } specs)
        {
            var cases = Path.Combine(specs, "conformance", "cases");
            return Directory.Exists(cases) ? Path.GetFullPath(cases) : null;
        }

        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir?.Parent is not null; dir = dir.Parent)
        {
            var cases = Path.Combine(dir.Parent.FullName, "blokky-specs", "conformance", "cases");
            if (Directory.Exists(cases))
            {
                return cases;
            }
        }

        return null;
    }
}
