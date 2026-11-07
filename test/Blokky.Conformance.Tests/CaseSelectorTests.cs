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
        new(features.ToHashSet(StringComparer.Ordinal), ["real"], ExternalStop: true, Seed: false);

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
