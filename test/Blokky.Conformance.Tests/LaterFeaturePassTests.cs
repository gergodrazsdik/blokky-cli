using Blokky.Conformance.Cases;
using Blokky.Conformance.Execution;
using Blokky.Conformance.Matching;

namespace Blokky.Conformance.Tests;

public class LaterFeaturePassTests
{
    private static readonly Matcher Matcher = new();

    private static ConformanceCase Case(string id, string expect) =>
        CaseLoader.Parse(id, $"title: t\nrules: [SY-1]\nfeatures: [log]\nplan: |\n  blokky: 1.0\n  log: x\nexpect:\n{expect}", "linux");

    private static AdapterOutcome Envelope(params string[] errors) =>
        BlokkyAdapter.Interpret(AdapterTests.Exited(3,
            $$"""{"status":"invalid","phase":"plan","errors":[{{string.Join(",", errors)}}]}"""));

    private const string NotSupported = """{"code":"BK_PLAN_NOT_SUPPORTED","message":"m","pos":"2:1","detail":{"feature":"log"}}""";

    private const string Sy57 = """{"code":"BK_PLAN_VALUE","message":"m","pos":"3:10","detail":{"rule":"SY-57"}}""";

    private const string Rf3 = """{"code":"BK_PLAN_INVALID","message":"m","pos":"4:1","detail":{"rule":"RF-3"}}""";

    private static LaterResult Judge(ConformanceCase c, AdapterOutcome outcome) => LaterFeaturePass.Judge(c, outcome, 0, 0, Matcher);

    [Test]
    public async Task Expected_errors_pass_with_not_supported_errors_beside_them()
    {
        var c = Case("x/a",
            "  plan_errors:\n    - { code: BK_PLAN_VALUE, detail: { rule: SY-57 }, line: 3, column: 10 }\n  exhaustive: true\n");

        await Assert.That(Judge(c, Envelope(Sy57, NotSupported)).Kind).IsEqualTo(LaterKind.Pass);
        await Assert.That(Judge(c, Envelope(NotSupported)).Kind).IsEqualTo(LaterKind.Fail);
    }

    [Test]
    public async Task Missing_errors_of_a_pending_family_are_pending_and_the_others_must_be_there()
    {
        var c = Case("x/b", "  plan_errors:\n    - { code: BK_PLAN_VALUE, detail: { rule: SY-57 }, line: 3 }\n"
            + "    - { code: BK_PLAN_INVALID, detail: { rule: RF-3 }, line: 4 }\n  exhaustive: true\n");

        var pending = Judge(c, Envelope(Sy57, NotSupported));
        var missingOwn = Judge(c, Envelope(NotSupported));
        var all = Judge(c, Envelope(Sy57, Rf3, NotSupported));

        await Assert.That(pending.Kind).IsEqualTo(LaterKind.Pending);
        await Assert.That(pending.Reason).Contains("RF-3 (T-0085)");
        await Assert.That(missingOwn.Kind).IsEqualTo(LaterKind.Fail);
        await Assert.That(all.Kind).IsEqualTo(LaterKind.Pass);
        await Assert.That(all.PendingKeys).Contains("RF-3");
    }

    [Test]
    public async Task A_pending_exhaustive_case_fails_on_an_error_it_does_not_expect()
    {
        var c = Case("x/c", "  plan_errors:\n    - { code: BK_PLAN_INVALID, detail: { rule: RF-3 }, line: 4 }\n  exhaustive: true\n");

        await Assert.That(Judge(c, Envelope(NotSupported)).Kind).IsEqualTo(LaterKind.Pending);
        await Assert.That(Judge(c, Envelope(Sy57, NotSupported)).Reason).Contains("doesn't expect");
    }

    [Test]
    public async Task Input_error_cases_are_not_applicable()
    {
        var c = Case("x/d", "  input_errors:\n    - { code: BK_INPUT_MISSING }\n");

        await Assert.That(Judge(c, Envelope(NotSupported)).Kind).IsEqualTo(LaterKind.NotApplicable);
    }

    [Test]
    public async Task An_entry_whose_cases_all_pass_is_stale()
    {
        LaterResult[] results =
        [
            new(LaterKind.Pass, null, ["RF-3", "SH-7"]),
            new(LaterKind.Pending, "p", ["SH-7"]),
        ];

        await Assert.That(string.Join(",", LaterFeaturePass.Stale(results).Select(e => e.Key))).IsEqualTo("RF-3");
    }

    [Test]
    public async Task Every_pending_entry_names_a_later_task_once()
    {
        var keys = PendingRuleFamilies.Entries.Select(e => e.Key).ToList();

        await Assert.That(keys.Count).IsEqualTo(keys.Distinct(StringComparer.Ordinal).Count());
        await Assert.That(PendingRuleFamilies.Entries.All(e => e.Task is "T-0082" or "T-0083" or "T-0084" or "T-0085" or "T-0086"))
            .IsTrue();
    }
}
