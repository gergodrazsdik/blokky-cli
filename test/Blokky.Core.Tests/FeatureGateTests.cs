using System.Collections.Frozen;
using System.Text;
using Blokky.Core.Errors;
using Blokky.Core.Features;
using Blokky.Core.Validation;

namespace Blokky.Core.Tests;

public class FeatureGateTests
{
    private static ValidationResult Validate(string plan, params string[] supported) =>
        new PlanValidator(supported.ToFrozenSet(StringComparer.Ordinal)).Validate(Encoding.UTF8.GetBytes(plan));

    private static string Features(string plan) =>
        string.Join(" ", Validate(plan).Features.Select(f => f.Feature).Order(StringComparer.Ordinal));

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
    public async Task Each_unsupported_feature_is_one_error_at_its_first_use_in_document_order()
    {
        var result = Validate("blokky: 1.0\ndo:\n  - log: a\n    when: { var: X, equals: b }\n  - log: c\n", "do");
        var errors = result.Errors.Select(e => $"{e.Detail!["feature"]} {e.Pos} {e.Node}");

        await Assert.That(result.Errors.All(e => e.Code == ErrorCodes.PlanNotSupported)).IsTrue();
        await Assert.That(string.Join("; ", errors)).IsEqualTo("log 3:5 /do/0; when 4:5 /do/0; cond-var 4:13 /do/0");
    }

    [Test]
    public async Task A_plan_whose_features_are_all_supported_is_valid()
    {
        var result = Validate("blokky: 1.0\nname: hi ${X}\nvars: { X: a }\nlog: hi\n", "log", "vars", "interpolation");

        await Assert.That(result.Ok).IsTrue();
        await Assert.That(result.Ast!.SpecVersion).IsEqualTo("1.0");
    }

    [Test]
    public async Task Gate_errors_come_after_and_alongside_the_other_plan_errors()
    {
        var result = Validate("blokky: 1.0\ntimeout: 30\nexec: [a]\n", "exec");
        var codes = result.Errors.Select(e => $"{e.Code} {e.Detail?["rule"] ?? e.Detail?["feature"]}");

        await Assert.That(string.Join("; ", codes)).IsEqualTo("BK_PLAN_VALUE SY-57; BK_PLAN_NOT_SUPPORTED timeout");
    }

    [Test]
    [Arguments("blokky: 1.0\nexec: [a]\n", "exec")]
    [Arguments("blokky: 1.0\nexec: a b\n", "exec-string")]
    [Arguments("blokky: 1.0\nscript: x\nshell: zsh\n", "script shell-zsh")]
    [Arguments("blokky: 1.0\nscript: x\n", "script")]
    [Arguments("blokky: 1.0\nshell: sh\nscript: [ -f x ]\n", "script shell-sh")]
    [Arguments("blokky: 1.0\nlog: x\nshell: sh\n", "log")]
    [Arguments("blokky: 1.0\ndefaults: { shell: sh }\nlog: x\n", "defaults log shell-sh")]
    [Arguments("blokky: 1.0\nexec: [a]\nkeep_descendants: detach\nstderr: merge\nok_exit: [1]\n",
        "exec keep-descendants-detach ok-exit stderr-merge")]
    [Arguments("blokky: 1.0\nexec: [a]\ncapture: { A: .a }\nsecret: true\n", "capture-fields exec secret")]
    [Arguments("blokky: 1.0\nexec: [a]\ncapture: A\nsecret: false\n", "capture exec")]
    [Arguments("blokky: 1.0\nexec: [a]\nretry: { result: exit, equals: '1' }\n", "cond-result exec retry-condition")]
    [Arguments("blokky: 1.0\nexec: [a]\nretry: true\nbackoff: { factor: 2 }\njitter: 0.5\n", "backoff exec jitter retry")]
    [Arguments("blokky: 1.0\nfor: { matrix: { A: [x] }, concurrency: 2 }\nlog: x\n", "for-concurrency for-matrix log")]
    [Arguments("blokky: 1.0\nset: { A: '${B}', C: { from_env: D, secret: true } }\n", "from-env interpolation secret-ref set")]
    [Arguments("blokky: 1.0\nset: { A: '$${B}' }\n", "set")]
    [Arguments("blokky: 1.0\nwhen: { not: { probe: { exec: a b }, json: ., gt: '1' } }\nlog: x\n",
        "cond-composite cond-json cond-numeric cond-probe exec-string log when")]
    [Arguments("blokky: 1.0\nservices: [{ name: s, exec: [a], ready: { port: 1, open: true } }]\nlog: x\n", "exec log services")]
    public async Task Derives_the_features_of_a_plan(string plan, string expected)
    {
        await Assert.That(Features(plan)).IsEqualTo(expected);
    }
}
