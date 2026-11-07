using System.Text;
using Blokky.Core.Errors;
using Blokky.Core.Features;
using Blokky.Core.Plans;

namespace Blokky.Core.Tests;

public class FeatureGateTests
{
    [Test]
    public async Task This_build_supports_no_features()
    {
        await Assert.That(FeatureSupport.Validated.Count).IsEqualTo(0);
        await Assert.That(FeatureSupport.Runnable.Count).IsEqualTo(0);
        await Assert.That(FeatureIds.All.Count).IsEqualTo(FeatureIds.All.Distinct(StringComparer.Ordinal).Count());
        await Assert.That(FeatureIds.Kinds.All(FeatureIds.IsKnown)).IsTrue();
    }

    [Test]
    public async Task Runnable_is_a_subset_of_validated_and_both_use_the_vocabulary()
    {
        // ADR 0009: run gates on runnable after validated, and a release needs the two tables equal.
        await Assert.That(FeatureSupport.Runnable.IsSubsetOf(FeatureSupport.Validated)).IsTrue();
        await Assert.That(FeatureSupport.Validated.All(FeatureIds.IsKnown)).IsTrue();
        await Assert.That(FeatureSupport.For(FeatureTable.Validated)).IsSameReferenceAs(FeatureSupport.Validated);
        await Assert.That(FeatureSupport.For(FeatureTable.Runnable)).IsSameReferenceAs(FeatureSupport.Runnable);
    }

    [Test]
    [Arguments("blokky: 1.0\nlog: hi\n", "log", 2)]
    [Arguments("blokky: 1.0\r\nname: x\r\ndo:\r\n  - log: hi\r\n", "do", 3)]
    [Arguments("exec: [git, status]\nblokky: 1.0\n", "exec", 1)]
    [Arguments("blokky: 1.0\nexec:\n  - git\n", "exec", 2)]
    [Arguments("blokky: 1.0\nexec: git status\n", "exec-string", 2)]
    [Arguments("﻿script: echo hi\nblokky: 1.0\n", "script", 1)]
    [Arguments("# log: commented\nblokky: 1.0\nif: { var: X }\n", "if", 3)]
    public async Task Names_the_root_kind_with_its_position(string plan, string feature, int line)
    {
        var errors = StubFeatureGate.Check(new PlanSource(Encoding.UTF8.GetBytes(plan), PlanSource.Stdin), FeatureTable.Runnable);

        await Assert.That(errors.Count).IsEqualTo(1);
        await Assert.That(errors[0].Code).IsEqualTo(ErrorCodes.PlanNotSupported);
        await Assert.That(errors[0].Detail!["feature"]!.GetValue<string>()).IsEqualTo(feature);
        await Assert.That(errors[0].Pos).IsEqualTo($"{line}:1");
        await Assert.That(errors[0].Node).IsEqualTo("/");
    }

    [Test]
    [Arguments("")]
    [Arguments("blokky: 1.0\n")]
    [Arguments("{ blokky: 1.0, log: hi }")]
    [Arguments("  log: indented\n")]
    public async Task Falls_back_to_a_fixed_vocabulary_id_without_a_position(string plan)
    {
        var errors = StubFeatureGate.Check(new PlanSource(Encoding.UTF8.GetBytes(plan), PlanSource.Stdin), FeatureTable.Runnable);
        var feature = errors[0].Detail!["feature"]!.GetValue<string>();

        await Assert.That(feature).IsEqualTo(StubFeatureGate.FallbackFeature);
        await Assert.That(FeatureIds.IsKnown(feature)).IsTrue();
        await Assert.That(errors[0].Pos).IsNull();
    }
}
