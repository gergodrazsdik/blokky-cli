using System.Collections.Frozen;
using System.Text;
using Blokky.Core.Features;
using Blokky.Core.Validation;
using Blokky.Core.Validation.Rules.Conditions;

namespace Blokky.Core.Tests;

public class ConditionRulesTests
{
    private static readonly FrozenSet<string> Everything = FeatureIds.All.ToFrozenSet(StringComparer.Ordinal);

    /// <summary>Each error as <c>code rule line:column node</c>, with the conditions family only.</summary>
    private static string Errors(string plan) =>
        string.Join("; ", new PlanValidator(Everything, rules: [new ConditionRules()]).Validate(Encoding.UTF8.GetBytes(plan)).Errors
            .Select(e => $"{e.Code} {e.Detail?["rule"] ?? e.Detail?["suggestion"] ?? "-"} {e.Pos} {e.Node}"));

    [Test]
    public async Task The_family_is_registered_once()
    {
        await Assert.That(PlanRules.All.Count(r => r is ConditionRules)).IsEqualTo(1);
    }

    [Test]
    [Arguments("blokky: 1.0\nwhen: { var: X, equals: a }\nlog: x\n", "")]
    [Arguments("blokky: 1.0\nwhen: { var: X, json: .a, equals: a, ignore_case: true }\nlog: x\n", "")]
    [Arguments("blokky: 1.0\nwhen: { probe: { exec: [a] } }\nlog: x\n", "")]
    [Arguments("blokky: 1.0\nwhen: { all: [ { var: X, equals: a }, { any: [ { env: Y, defined: true } ] } ] }\nlog: x\n", "")]
    [Arguments("blokky: 1.0\nwhen: { var: X }\nlog: x\n", "BK_PLAN_INVALID CO-4 2:9 /")]
    [Arguments("blokky: 1.0\nwhen: { equals: a }\nlog: x\n", "BK_PLAN_INVALID CO-4 2:7 /")]
    [Arguments("blokky: 1.0\nwhen: { json: .a }\nlog: x\n", "BK_PLAN_INVALID CO-4 2:7 /")]
    [Arguments("blokky: 1.0\nwhen: { var: X, env: Y, equals: a }\nlog: x\n", "BK_PLAN_INVALID CO-4 2:17 /")]
    [Arguments("blokky: 1.0\nwhen: { var: X, equals: a, contains: b }\nlog: x\n", "BK_PLAN_INVALID CO-4 2:28 /")]
    [Arguments("blokky: 1.0\nwhen: { all: [ { var: X, equals: a } ], var: X }\nlog: x\n", "BK_PLAN_INVALID CO-4 2:41 /")]
    [Arguments("blokky: 1.0\nwhen: { all: [ { var: X, equals: a } ], any: [ { var: X, equals: a } ] }\nlog: x\n",
        "BK_PLAN_INVALID CO-4 2:41 /")]
    [Arguments("blokky: 1.0\nwhen: { probe: { exec: [a] }, json: .a }\nlog: x\n", "BK_PLAN_INVALID CO-4 2:31 /")]
    [Arguments("blokky: 1.0\nwhen: { exec: [a] }\nlog: x\n", "BK_PLAN_INVALID CO-4 2:9 /")]
    [Arguments("blokky: 1.0\ndo:\n  - when: { any: [ { do: [ { log: a } ] } ] }\n    log: x\n", "BK_PLAN_INVALID CO-4 3:22 /do/0")]
    [Arguments("blokky: 1.0\nwhen: { all: [ { var: X } ] }\nlog: x\n", "BK_PLAN_INVALID CO-4 2:18 /")]
    public async Task Shape(string plan, string expected)
    {
        await Assert.That(Errors(plan)).IsEqualTo(expected);
    }

    [Test]
    [Arguments("blokky: 1.0\nwhen: { var: X, gt: '1', ignore_case: true }\nlog: x\n", "BK_PLAN_INVALID CO-12 2:26 /")]
    [Arguments("blokky: 1.0\nwhen: { ignore_case: false, path: a, exists: true }\nlog: x\n", "BK_PLAN_INVALID CO-12 2:38 /")]
    [Arguments("blokky: 1.0\nwhen: { var: X, not_matches: 'a', ignore_case: true }\nlog: x\n", "")]
    [Arguments("blokky: 1.0\nwhen: { var: X, not_in: [a], ignore_case: true }\nlog: x\n", "")]
    public async Task Ignore_case(string plan, string expected)
    {
        await Assert.That(Errors(plan)).IsEqualTo(expected);
    }

    [Test]
    [Arguments("blokky: 1.0\nwhen: { probe: { script: x, shell: bash } }\nlog: x\n", "")]
    [Arguments("blokky: 1.0\nwhen: { probe: { exec: [a], script: x } }\nlog: x\n", "BK_PLAN_INVALID CO-26 2:29 /")]
    [Arguments("blokky: 1.0\nwhen: { probe: { script: x, exec: [a] } }\nlog: x\n", "BK_PLAN_INVALID CO-26 2:29 /")]
    [Arguments("blokky: 1.0\nwhen: { probe: { exec: [a], shell: bash } }\nlog: x\n", "BK_PLAN_INVALID CO-26 2:29 /")]
    [Arguments("blokky: 1.0\nwhen: { probe: { timeout: 5s } }\nlog: x\n", "BK_PLAN_INVALID CO-26 2:16 /")]
    public async Task Probe_shape(string plan, string expected)
    {
        await Assert.That(Errors(plan)).IsEqualTo(expected);
    }

    [Test]
    [Arguments("blokky: 1.0\nwhen: { port: 8080, open: true }\nlog: x\n", "BK_PLAN_INVALID CO-44 2:9 /")]
    [Arguments("blokky: 1.0\nuntil: { all: [ { output: stdout, contains: a } ] }\nmax: 2\nexec: [a]\n", "BK_PLAN_INVALID CO-44 2:19 /")]
    [Arguments(
        "blokky: 1.0\nservices:\n  - name: db\n    exec: [a]\n    ready: { any: [ { port: 5432, open: true }, "
        + "{ output: stderr, matches: 'up' } ] }\nexec: [b]\n", "")]
    public async Task Service_only_subjects(string plan, string expected)
    {
        await Assert.That(Errors(plan)).IsEqualTo(expected);
    }

    [Test]
    [Arguments("blokky: 1.0\nwhen: { var: X, exists: true }\nlog: x\n", "BK_PLAN_INVALID CO-47 2:17 /")]
    [Arguments("blokky: 1.0\nwhen: { path: a, equals: a }\nlog: x\n", "BK_PLAN_INVALID CO-47 2:18 /")]
    [Arguments("blokky: 1.0\nwhen: { host: os, contains: lin }\nlog: x\n", "BK_PLAN_INVALID CO-47 2:19 /")]
    [Arguments("blokky: 1.0\nwhen: { env: X, open: true }\nlog: x\n", "BK_PLAN_INVALID CO-47 2:17 /")]
    [Arguments("blokky: 1.0\nwhen: { command: git, is_file: true }\nlog: x\n", "BK_PLAN_INVALID CO-47 2:23 /")]
    [Arguments("blokky: 1.0\nuntil: { result: exit, contains: '0' }\nmax: 2\nexec: [a]\n", "BK_PLAN_INVALID CO-47 2:24 /")]
    [Arguments("blokky: 1.0\nuntil: { result: error, gt: '0' }\nmax: 2\nexec: [a]\n", "BK_PLAN_INVALID CO-47 2:25 /")]
    [Arguments("blokky: 1.0\nuntil: { result: exit, empty: true }\nmax: 2\nexec: [a]\n", "BK_PLAN_INVALID CO-47 2:24 /")]
    [Arguments("blokky: 1.0\nuntil: { result: exit, ge: '1' }\nmax: 2\nexec: [a]\n", "")]
    [Arguments("blokky: 1.0\nuntil: { result: error, empty: true }\nmax: 2\nexec: [a]\n", "")]
    [Arguments("blokky: 1.0\nwhen: { probe: { exec: [a] }, lt: '3' }\nlog: x\n", "")]
    [Arguments("blokky: 1.0\nwhen: { command: git, not_exists: true }\nlog: x\n", "")]
    public async Task Operator_and_subject_pairs(string plan, string expected)
    {
        await Assert.That(Errors(plan)).IsEqualTo(expected);
    }

    [Test]
    [Arguments("blokky: 1.0\nwhile: { result: exit, equals: '1' }\nmax: 3\nexec: [a]\n", "BK_PLAN_INVALID RF-13 2:10 /")]
    [Arguments("blokky: 1.0\nwhen: { result: error, equals: '' }\nexec: [a]\n", "BK_PLAN_INVALID RF-13 2:9 /")]
    [Arguments("blokky: 1.0\nuntil: { result: stdout, equals: done }\nmax: 3\ndo:\n  - exec: [a]\n", "BK_PLAN_INVALID RF-13 2:18 /")]
    [Arguments("blokky: 1.0\nretry: { result: error, equals: BK_EXIT }\nmax: 2\ndo:\n  - exec: [a]\n", "")]
    [Arguments("blokky: 1.0\nretry: { any: [ { result: stderr, contains: x } ] }\nmax: 2\nscript: x\n", "")]
    public async Task Result_subject(string plan, string expected)
    {
        await Assert.That(Errors(plan)).IsEqualTo(expected);
    }

    [Test]
    [Arguments("blokky: 1.0\nwhen: { not: { var: X, equals: a } }\nlog: x\n", "BK_PLAN_INVALID RF-14 2:9 /")]
    [Arguments("blokky: 1.0\nwhen: { not: { env: X, not_defined: true } }\nlog: x\n", "BK_PLAN_INVALID RF-14 2:9 /")]
    [Arguments("blokky: 1.0\nwhen: { any: [ { not: { path: a, exists: true } } ] }\nlog: x\n", "BK_PLAN_INVALID RF-14 2:18 /")]
    [Arguments("blokky: 1.0\nwhen: { not: { path: a, is_dir: true } }\nlog: x\n", "")]
    [Arguments("blokky: 1.0\nwhen: { not: { var: X, gt: '1' } }\nlog: x\n", "")]
    [Arguments("blokky: 1.0\nwhen: { not: { all: [ { var: X, equals: a } ] } }\nlog: x\n", "")]
    [Arguments("blokky: 1.0\nwhen: { not: { probe: { exec: [a] } } }\nlog: x\n", "")]
    public async Task Not_around_a_simple_condition(string plan, string expected)
    {
        await Assert.That(Errors(plan)).IsEqualTo(expected);
    }

    [Test]
    public async Task Every_error_is_reported_in_one_pass()
    {
        const string plan = "blokky: 1.0\ndo:\n  - when: { var: X, env: Y, exists: true, ignore_case: true }\n    log: a\n"
            + "  - while: { port: 1, open: true }\n    max: 2\n    exec: [a]\n";
        await Assert.That(Errors(plan)).IsEqualTo(
            "BK_PLAN_INVALID CO-4 3:21 /do/0; BK_PLAN_INVALID CO-47 3:29 /do/0; BK_PLAN_INVALID CO-12 3:43 /do/0; "
            + "BK_PLAN_INVALID CO-44 5:14 /do/1");
    }
}
