using System.Collections.Frozen;
using Blokky.Conformance.Cases;
using Blokky.Conformance.Execution;
using Blokky.Conformance.Matching;
using Blokky.Conformance.Yaml;
using Blokky.Core.Ast;
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

    /// <summary>Cases whose plan is valid: run and no-result cases, and validation cases without <c>plan_errors</c>.</summary>
    public static IEnumerable<string> ValidPlanCases() => CaseIds(c => OnThisOs(c) && !ExpectsPlanErrors(c));

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
    /// A valid plan gets no plan error but <c>BK_PLAN_NOT_SUPPORTED</c> (with the validated table empty, one per feature):
    /// otherwise the not-supported skip would hide a wrong error, as the runner's later-feature pass checks too.
    /// </summary>
    [Test]
    [MethodDataSource(nameof(ValidPlanCases))]
    public async Task Valid_plans_get_only_not_supported_errors(string id)
    {
        var c = LoadOrSkip(id);
        var stray = ValidatorFor(c).Validate(c.Plan).Errors.Where(e => e.Code != ErrorCodes.PlanNotSupported)
            .Select(e => $"{e.Code} {e.Detail?["rule"]} {e.Pos}: {e.Message}").ToList();

        await Assert.That(string.Join("\n", stray)).IsEqualTo("");
        await Assert.That(Judge(c).Kind).IsNotEqualTo(LaterKind.Fail);
    }

    /// <summary>Every pending entry applies to some validation case, so an entry no case needs (a typo, a dead key) fails.</summary>
    [Test]
    public async Task Every_pending_entry_applies_to_a_case()
    {
        if (ReaderCaseTests.CasesDir() is not { } dir)
        {
            Skip.Test("no blokky-specs checkout: set BLOKKY_SPECS or check it out next to blokky-cli");
            return;
        }

        var used = AllCases(dir).Where(c => c.ExpectKind == ExpectKind.Validation).SelectMany(PendingKeys)
            .ToHashSet(StringComparer.Ordinal);
        var dead = PendingRuleFamilies.Entries.Select(e => e.Key).Where(k => !used.Contains(k)).ToList();

        await Assert.That(string.Join(", ", dead)).IsEqualTo("");
    }

    /// <summary>
    /// ADR 0009: a feature enters the validated table only once no validation case using it depends on a pending entry;
    /// otherwise an invalid plan using it would validate clean. The real table is checked; an injected one shows that the
    /// check finds such cases (the real table is empty until T-0086).
    /// </summary>
    [Test]
    public async Task No_validated_feature_has_a_pending_validation_case()
    {
        if (ReaderCaseTests.CasesDir() is not { } dir)
        {
            Skip.Test("no blokky-specs checkout: set BLOKKY_SPECS or check it out next to blokky-cli");
            return;
        }

        var cases = AllCases(dir);
        var pendingFeature = cases.First(c => c.ExpectKind == ExpectKind.Validation && PendingKeys(c).Count > 0).Features[0];

        await Assert.That(string.Join("\n", ValidatedButPending(FeatureSupport.Validated, cases))).IsEqualTo("");
        await Assert.That(ValidatedButPending(new HashSet<string> { pendingFeature }, cases)).IsNotEmpty();
        await Assert.That(ValidatedButPending(new HashSet<string>(), cases)).IsEmpty();
    }

    /// <summary>Validation cases that list a feature of <paramref name="validated"/> and expect an error of a pending entry.</summary>
    private static List<string> ValidatedButPending(IReadOnlySet<string> validated, IEnumerable<ConformanceCase> cases) =>
        [.. cases.Where(c => c.ExpectKind == ExpectKind.Validation)
            .SelectMany(c => c.Features.Where(validated.Contains).Select(f => (Case: c, Feature: f)))
            .Where(x => PendingKeys(x.Case).Count > 0)
            .Select(x => $"{x.Feature}: {x.Case.Id} pends on {string.Join(", ", PendingKeys(x.Case).Distinct(StringComparer.Ordinal))}")];

    /// <summary>
    /// CF-11: the features derived from each plan, against the case's list. The runner ids come from <c>actions</c>,
    /// as in blokky-specs <c>scripts/check.py</c>. Shell ids are derived only where a <c>script</c> and an explicit shell
    /// (its own or <c>defaults.shell</c>) pair up, so a case may list a shell id the derivation lacks only when they don't:
    /// a script without a named shell (it runs in the configured or detected one) or a <c>shell</c> without a script (an
    /// RF-32 companion error). <c>check.py</c> excuses shell ids always, this test only then.
    /// The remaining differences are findings (tracker F-0033), listed here so a new one fails the test.
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
        foreach (var c in AllCases(dir))
        {
            var result = Validator.Validate(c.Plan);
            if (result.Ast is null)
            {
                continue; // a plan the reader can't continue with has no derivable features (as in check.py)
            }

            var derived = result.Features.Select(f => f.Feature).ToHashSet(StringComparer.Ordinal);
            derived.UnionWith(c.Actions.Select(a => a.Kill ? "kill" : "external-stop"));
            var missing = derived.Except(c.Features).Order(StringComparer.Ordinal).ToList();
            var unpaired = ShellUnpaired(result.Ast);
            var extra = c.Features.Except(derived).Where(f => !(unpaired && f.StartsWith("shell-", StringComparison.Ordinal)))
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

    /// <summary>A validator that reads the case's environment, as the runner's invocation does (CF-56, VA-40).</summary>
    private static PlanValidator ValidatorFor(ConformanceCase c) =>
        c.Env.Count == 0
            ? Validator
            : new(FrozenSet<string>.Empty,
                environment: name => c.Env.TryGetValue(name, out var value) ? value : Environment.GetEnvironmentVariable(name));

    private static LaterResult Judge(ConformanceCase c) =>
        LaterFeaturePass.Judge(c, Interpret(ValidatorFor(c).Validate(c.Plan).Errors), 0, 0, Matcher);

    /// <summary>
    /// Whether a node, service or probe has a script in a shell the plan doesn't name, or a <c>shell</c> without a script.
    /// </summary>
    private static bool ShellUnpaired(PlanAst plan)
    {
        bool Unpaired(MappingObject process, Typed<ShellName>? shell) => process.Key("script") is null
            ? shell is not null
            : shell is null && plan.Defaults?.Shell is null;

        return PlanWalk.Nodes(plan).Any(n => Unpaired(n, n.Shell)
            || (n.Services ?? []).Any(s => Unpaired(s, s.Shell))
            || PlanWalk.Conditions(n).SelectMany(x => PlanWalk.Within(x.Condition)).SelectMany(x => x.Subjects)
                .Any(x => x.Probe is { } probe && Unpaired(probe, probe.Shell)));
    }

    private static bool ExpectsPlanErrors(ConformanceCase c) =>
        c.ExpectKind == ExpectKind.Validation && c.Expect.GetValueOrDefault("plan_errors") is List<object?> { Count: > 0 };

    private static List<ConformanceCase> AllCases(string dir) =>
        [.. CaseLoader.FindCaseFiles(dir).Select(f => TryLoad(dir, f)).OfType<ConformanceCase>()];

    /// <summary>
    /// Whether the case runs on this host (CF-33): environment names compare by the host's rules (VA-6), so a case for
    /// another OS can expect what this host doesn't report.
    /// </summary>
    private static bool OnThisOs(ConformanceCase c) =>
        c.Requires.Os.Count == 0
        || c.Requires.Os.Contains(OperatingSystem.IsWindows() ? "windows" : OperatingSystem.IsMacOS() ? "macos" : "linux");

    private static bool Judged(ConformanceCase c) =>
        OnThisOs(c) && c.ExpectKind == ExpectKind.Validation
        && c.Expect.GetValueOrDefault("input_errors") is not List<object?> { Count: > 0 };

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
