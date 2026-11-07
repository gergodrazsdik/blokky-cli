using Blokky.Conformance.Cases;
using Blokky.Conformance.Execution;

namespace Blokky.Conformance.Tests;

public class CaseSelectorTests
{
    private static readonly HostInfo Linux = new(
        "linux",
        new Dictionary<string, string> { ["sh"] = "/usr/bin", ["bash"] = "/usr/bin" },
        new Dictionary<string, string>(),
        "/opt/blokky-testkit",
        false);

    private static AdapterCapabilities Supporting(params string[] features) =>
        new(features.ToHashSet(StringComparer.Ordinal), features.ToHashSet(StringComparer.Ordinal), ["real"], ExternalStop: true,
            Seed: false);

    private static ConformanceCase Case(string features, string extra = "") =>
        CaseLoader.Parse("a/b", $"title: t\nrules: [EM-1]\nfeatures: {features}\n{Plan}expect: {{ status: succeeded }}\n{extra}", "linux");

    private const string Plan = "plan: |\n  blokky: 1.0\n  exec: [blokky-testkit, exit, '0']\n";

    [Test]
    public async Task Unsupported_features_skip_the_case_with_every_missing_id()
    {
        var s = CaseSelector.Select(Case("[exec, timeout, when]"), Linux, Supporting("exec"));

        await Assert.That(s.Skip).IsEqualTo(SkipKind.NotSupported);
        await Assert.That(s.Reason).IsEqualTo("not supported: timeout, when");
        await Assert.That(s.Unsupported).IsEquivalentTo(["timeout", "when"]);
    }

    [Test]
    public async Task Validation_cases_use_the_validated_table_and_run_cases_the_runnable_one()
    {
        // ADR 0009: `exec` is validated but not runnable yet.
        var caps = new AdapterCapabilities(new HashSet<string> { "exec" }, new HashSet<string>(), ["real"], true, false);
        var validation = CaseLoader.Parse("a/v",
            $"title: t\nrules: [EM-1]\nfeatures: [exec]\n{Plan}expect: {{ plan_errors: [{{ code: BK_PLAN_VALUE }}] }}\n", "linux");

        await Assert.That(CaseSelector.Select(validation, Linux, caps).Skip).IsNull();
        var run = CaseSelector.Select(Case("[exec]"), Linux, caps);
        await Assert.That(run.Skip).IsEqualTo(SkipKind.NotSupported);
        await Assert.That(run.Reason).IsEqualTo("not supported: exec");
        await Assert.That(caps.FeaturesFor(ExpectKind.NoResult)).IsSameReferenceAs(caps.Runnable);
    }

    [Test]
    public async Task This_builds_adapter_declares_cores_tables()
    {
        await Assert.That(AdapterCapabilities.Current.Validated).IsSameReferenceAs(Blokky.Core.Features.FeatureSupport.Validated);
        await Assert.That(AdapterCapabilities.Current.Runnable).IsSameReferenceAs(Blokky.Core.Features.FeatureSupport.Runnable);
    }

    [Test]
    public async Task Requirements_come_before_features()
    {
        var s = CaseSelector.Select(Case("[exec]", "requires: { os: [windows] }"), Linux, Supporting());

        await Assert.That(s.Skip).IsEqualTo(SkipKind.Requirement);
        await Assert.That(s.Reason).IsEqualTo("requirement: os");
    }

    [Test]
    [Arguments("requires: { shells: [pwsh] }", "requirement: shells (pwsh not installed)")]
    [Arguments("requires: { container-engine: true }", "requirement: container-engine")]
    [Arguments("clock: [virtual]", "requirement: clock")]
    [Arguments("platforms: { linux: { skip: no } }", "platform: no")]
    public async Task Skips_name_their_reason(string extra, string reason)
    {
        var s = CaseSelector.Select(Case("[exec]", extra), Linux, Supporting("exec"));

        await Assert.That(s.Reason).IsEqualTo(reason);
    }

    [Test]
    public async Task A_missing_testkit_skips_cases_that_would_run()
    {
        var s = CaseSelector.Select(Case("[exec]"), Linux with { Testkit = null }, Supporting("exec"));

        await Assert.That(s.Skip).IsEqualTo(SkipKind.Requirement);
        await Assert.That(s.Reason).StartsWith("requirement: testkit");
    }

    [Test]
    public async Task A_missing_testkit_also_skips_the_not_supported_check()
    {
        // Without the testkit the log stays empty, so "no process started" would pass vacuously (CF-12).
        var s = CaseSelector.Select(Case("[exec, timeout]"), Linux with { Testkit = null }, Supporting("exec"));

        await Assert.That(s.Skip).IsEqualTo(SkipKind.Requirement);
        await Assert.That(s.Reason).StartsWith("requirement: testkit");
    }

    [Test]
    public async Task Stop_actions_need_an_adapter_that_can_stop_a_run()
    {
        var stop = Case("[exec, external-stop]", "actions: [{ after: 1s, stop: true }]");
        var kill = Case("[exec, kill]", "actions: [{ after: 1s, kill: true }]");
        var noStop = Supporting("exec", "external-stop", "kill") with { ExternalStop = false };

        await Assert.That(CaseSelector.Select(stop, Linux, noStop).Reason).StartsWith("requirement: external stop");
        await Assert.That(CaseSelector.Select(stop, Linux, noStop with { ExternalStop = true }).Skip).IsNull();
        await Assert.That(CaseSelector.Select(kill, Linux, noStop).Skip).IsNull();
    }

    [Test]
    public async Task The_fastest_clock_both_sides_support_is_used()
    {
        var caps = Supporting("exec") with { ClockModes = ["real", "scaled"] };

        await Assert.That(CaseSelector.Select(Case("[exec]"), Linux, caps).Clock).IsEqualTo("scaled");
        await Assert.That(CaseSelector.Select(Case("[exec]", "clock: [real]"), Linux, caps).Clock).IsEqualTo("real");
        await Assert.That(CaseSelector.Select(Case("[exec]"), Linux, Supporting("exec")).Clock).IsEqualTo("real");
    }

    [Test]
    public async Task Process_heavy_cases_default_to_the_real_clock()
    {
        await Assert.That(CaseSelector.ClockModes(Case("[exec]"))).IsEquivalentTo(["real", "scaled"]);
        await Assert.That(CaseSelector.ClockModes(Case("[exec]", "actions: [{ after: 1s, stop: true }]"))).IsEquivalentTo(["real"]);
        await Assert.That(CaseSelector.ClockModes(Case("[exec]", "programs: { daemon: [spawn, --, sleep, 1m] }"))).IsEquivalentTo(["real"]);
    }
}
