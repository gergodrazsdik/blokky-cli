using System.Collections.Frozen;
using Blokky.Conformance.Cases;
using Blokky.Conformance.Execution;
using Blokky.Conformance.Matching;
using Blokky.Conformance.Yaml;
using Blokky.Core.Contracts;
using Blokky.Core.Errors;
using Blokky.Core.Features;
using Blokky.Core.Validation;

namespace Blokky.Conformance.Tests;

/// <summary>
/// The validator against the blokky-specs suite, in-process, judged like the runner's later-feature validation pass
/// (<see cref="LaterFeaturePass"/>): validation cases whose expected errors are all schema level (none falls under
/// <see cref="PendingRuleFamilies"/>) must pass; no validation case may fail. Also compares each case's derived features
/// with its <c>features</c> list (CF-11). Without a blokky-specs checkout the tests skip.
/// </summary>
public class ValidatorCaseTests
{
    private const string NoSpecs = "(no blokky-specs checkout)";

    private static readonly Matcher Matcher = new();

    /// <summary>Validation cases with no input errors and no expected error of a pending rule family.</summary>
    public static IEnumerable<string> SchemaLevelCases() => CaseIds(c => Judged(c) && PendingKeys(c).Count == 0);

    /// <summary>Validation cases with no input errors whose expected errors include a pending rule family's.</summary>
    public static IEnumerable<string> PendingCases() => CaseIds(c => Judged(c) && PendingKeys(c).Count > 0);

    [Test]
    [MethodDataSource(nameof(SchemaLevelCases))]
    public async Task Schema_level_cases_pass(string id)
    {
        var c = LoadOrSkip(id);
        var result = Judge(c);

        await Assert.That(result.Reason).IsNull();
        await Assert.That(result.Kind).IsEqualTo(LaterKind.Pass);
    }

    [Test]
    [MethodDataSource(nameof(PendingCases))]
    public async Task Cases_of_pending_rule_families_report_no_wrong_errors(string id)
    {
        var c = LoadOrSkip(id);
        var result = Judge(c);

        await Assert.That(result.Kind == LaterKind.Fail ? result.Reason : null).IsNull();
    }

    /// <summary>
    /// CF-11: the features derived from each plan, against the case's list. The shell ids aren't derived for a script
    /// without <c>shell:</c>, and the runner ids come from <c>actions</c>, as in blokky-specs <c>scripts/check.py</c>. The
    /// remaining differences are findings (tracker F-0033), listed here so a new one fails the test.
    /// </summary>
    [Test]
    public async Task Derived_features_match_the_cases_lists()
    {
        if (ReaderCaseTests.CasesDir() is not { } dir)
        {
            Skip.Test("no blokky-specs checkout: set BLOKKY_SPECS or check it out next to blokky-cli");
            return;
        }

        var mismatches = new List<string>();
        foreach (var c in CaseLoader.FindCaseFiles(dir).Select(f => TryLoad(dir, f)).OfType<ConformanceCase>())
        {
            var result = Validator.Validate(c.Plan);
            if (result.Ast is null)
            {
                continue; // a plan the reader can't continue with has no derivable features (as in check.py)
            }

            var derived = result.Features.Select(f => f.Feature).ToHashSet(StringComparer.Ordinal);
            derived.UnionWith(c.Actions.Select(a => a.Kill ? "kill" : "external-stop"));
            var missing = derived.Except(c.Features).Order(StringComparer.Ordinal).ToList();
            var extra = c.Features.Except(derived).Where(f => !f.StartsWith("shell-", StringComparison.Ordinal))
                .Order(StringComparer.Ordinal).ToList();
            if (missing.Count + extra.Count > 0)
            {
                mismatches.Add($"{c.Id}: derived +[{string.Join(", ", missing)}] -[{string.Join(", ", extra)}]");
            }
        }

        await Assert.That(string.Join("\n", mismatches)).IsEqualTo(string.Join("\n", KnownFeatureMismatches));
    }

    /// <summary>Feature differences between the derivation and the cases' lists, reported as findings (F-0033).</summary>
    private static readonly string[] KnownFeatureMismatches = [];

    private static readonly PlanValidator Validator = new(FrozenSet<string>.Empty);

    private static LaterResult Judge(ConformanceCase c) =>
        LaterFeaturePass.Judge(c, Interpret(Validator.Validate(c.Plan).Errors), 0, 0, Matcher);

    private static bool Judged(ConformanceCase c) =>
        c.ExpectKind == ExpectKind.Validation && c.Expect.GetValueOrDefault("input_errors") is not List<object?> { Count: > 0 };

    private static List<string> PendingKeys(ConformanceCase c) =>
        [.. (c.Expect.GetValueOrDefault("plan_errors") as List<object?> ?? []).OfType<OrderedDictionary<string, object?>>()
            .Select(e => PendingRuleFamilies.For(c.Id, e.GetValueOrDefault("code") as string,
                (e.GetValueOrDefault("detail") as OrderedDictionary<string, object?>)?.GetValueOrDefault("rule") as string))
            .OfType<PendingEntry>().Select(e => e.Key)];

    /// <summary>The validator's errors as the adapter sees them in a pre-run envelope (exit 3, phase plan).</summary>
    private static AdapterOutcome Interpret(IReadOnlyList<BlokkyError> errors) =>
        errors.Count == 0
            ? BlokkyAdapter.Interpret(AdapterTests.Exited(0, """{"schema_version":"0.1","status":"valid","decision":"allow"}"""))
            : BlokkyAdapter.Interpret(new ProcessOutcome(3, ContractWriter.ToUtf8(new PreRunEnvelope(Phase.Plan, errors)), [], false,
                false, TimeSpan.Zero));

    private static ConformanceCase LoadOrSkip(string id)
    {
        if (id == NoSpecs || ReaderCaseTests.CasesDir() is not { } dir)
        {
            Skip.Test("no blokky-specs checkout: set BLOKKY_SPECS or check it out next to blokky-cli");
            throw new InvalidOperationException("unreachable");
        }

        return CaseLoader.Load(dir, Path.Combine(dir, id + ".yaml"), "linux");
    }

    private static List<string> CaseIds(Func<ConformanceCase, bool> select)
    {
        if (ReaderCaseTests.CasesDir() is not { } dir)
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
            return null;
        }
    }
}
