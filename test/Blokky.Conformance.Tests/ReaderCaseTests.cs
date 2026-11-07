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
    /// A fixed-seed mutation fuzz over the suite's plans (spike 3): edits with YAML punctuation must never make the reader
    /// throw or hang, and every error it reports carries a code and a position inside the text.
    /// </summary>
    [Test]
    public async Task Mutated_plans_never_crash_or_hang_the_reader()
    {
        if (CasesDir() is not { } dir)
        {
            Skip.Test("no blokky-specs checkout: set BLOKKY_SPECS or check it out next to blokky-cli");
            return;
        }

        var plans = CaseLoader.FindCaseFiles(dir).Select(f => TryLoad(dir, f)).OfType<ConformanceCase>().Select(c => c.Plan).ToList();
        var random = new Random(20261107);
        byte[] punctuation = "[]{}:,-?&*!|>'\"#%@`\t\r\n \\"u8.ToArray();
        var reader = new PlanReader();
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

            try
            {
                var result = reader.Read([.. plan]);
                if (result.Errors.Any(e => e.Pos is null || e.Code.Length == 0))
                {
                    problems.Add($"#{i}: an error without a position");
                }
            }
#pragma warning disable CA1031 // Any exception is the finding.
            catch (Exception ex)
#pragma warning restore CA1031
            {
                problems.Add($"#{i}: {ex.GetType().Name}: {ex.Message}");
            }
        }

        await Assert.That(string.Join("\n", problems.Take(5))).IsEqualTo("");
        await Assert.That(clock.Elapsed).IsLessThan(TimeSpan.FromSeconds(60));
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
