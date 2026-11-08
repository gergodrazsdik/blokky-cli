using System.Collections.Frozen;
using System.Globalization;
using System.Text;
using Blokky.Core.Errors;
using Blokky.Core.Features;
using Blokky.Core.Validation;
using Blokky.Core.Validation.Rules.Variables;

namespace Blokky.Core.Tests;

public class InterpolationSyntaxTests
{
    [Test]
    [Arguments("plain", "")]
    [Arguments("a ${X} b ${Y_2}", "X@2 Y_2@9")]
    [Arguments("$${X} and ${Y}", "Y@10")]
    [Arguments("$$${X}", "")]
    [Arguments("${lower} ${X", "lower!@0 X?@9")]
    [Arguments("cost $5 {x} $", "")]
    [Arguments("${}", "!@0")]
    public async Task Placeholders_and_escapes(string text, string expected)
    {
        var found = InterpolationSyntax.Parse(text).Select(p =>
            $"{p.Content}{(p.Terminated ? p.IsVariable ? "" : "!" : "?")}@{p.Offset}");

        await Assert.That(string.Join(" ", found)).IsEqualTo(expected);
    }

    [Test]
    public async Task Variables_lists_only_well_formed_names()
    {
        await Assert.That(string.Join(",", InterpolationSyntax.Variables("${A}-${b}-${A}-$${C}"))).IsEqualTo("A,A");
    }
}

public class VariableRulesTests
{
    private static readonly FrozenSet<string> Everything = FeatureIds.All.ToFrozenSet(StringComparer.Ordinal);

    /// <summary>The variables family's errors as <c>code rule line:column</c> (rule or <c>detail.name</c>).</summary>
    private static string Errors(string plan, Func<string, string?>? environment = null) =>
        string.Join("; ", new PlanValidator(Everything, rules: [new VariableRules()], environment: environment ?? (_ => null))
            .Validate(Encoding.UTF8.GetBytes("blokky: 1.0\n" + plan)).Errors
            .Select(e => $"{e.Code} {e.Detail?["rule"] ?? e.Detail?["name"]} {e.Pos}"));

    [Test]
    [Arguments("vars: { X: a }\nif: { var: X, equals: a }\nthen:\n  - set: { M: a }\nelse:\n  - set: { M: b }\n")]
    [Arguments("do:\n  - any:\n      - set: { M: a }\n      - set: { M: b }\n  - log: ${M}\n")]
    [Arguments("do:\n  - exec: [a]\n    until: { var: OUT, equals: x }\n    max: 2\n    capture: OUT\n  - log: ${OUT}\n")]
    [Arguments("do:\n  - for: { each: [a], as: I }\n    exec: [a, { var: I }]\n    capture: C\n    collect: { C: LIST }\n"
        + "  - log: ${LIST}\n  - for: { each: [b], as: I }\n    log: ${I_INDEX}\n")]
    [Arguments("tempdir: { as: T }\ndo:\n  - exec: [a, { var: T }]\n    capture: C\n  - log: ${C}\n")]
    [Arguments("do:\n  - exec: [a]\n    finally:\n      - exec: [b]\n        capture: F\n      - log: ${F}\n")]
    [Arguments("inputs: { A: { default: x } }\nvars:\n  B: ${A}\n  C: { var: B }\n  D: { from_env: HOME }\nlog: '$${Z} ${D}'\n")]
    [Arguments("for: { matrix: { OS: [a], ARCH: [b] } }\nlog: ${OS} ${ARCH} ${MATRIX_INDEX}\n")]
    [Arguments("retry: { var: OUT, equals: x }\nindex: N\nmax: 2\nexec: [a, { var: N }]\ncapture: OUT\n")]
    public async Task Valid_scopes_report_nothing(string plan)
    {
        await Assert.That(Errors(plan, name => name == "HOME" ? "/home/x" : null)).IsEqualTo("");
    }

    [Test]
    [Arguments("vars: { X: a }\nif: { var: X, equals: a }\nthen:\n  - set: { M: a }\nelse:\n  - log: ${M}\n", "RF-23 7:10")]
    [Arguments("do:\n  - for: { each: [a], as: I }\n    exec: [a]\n    capture: C\n  - log: ${C}\n", "RF-23 6:10")]
    [Arguments("do:\n  - exec: [a]\n    finally:\n      - exec: [b]\n        capture: F\n  - log: ${F}\n", "RF-16 7:10")]
    [Arguments("exec: [a]\nenv: { X: { var: OUT } }\ncapture: OUT\n", "RF-6 3:18")]
    [Arguments("tempdir: { as: T }\ncwd: { var: T }\nexec: [a]\n", "RF-6 3:13")]
    [Arguments("vars:\n  A: ${B}\n  B: x\nlog: x\n", "RF-27 3:6")]
    [Arguments("vars:\n  A: ${NOPE}\nlog: x\n", "RF-23 3:6")]
    [Arguments("outputs: [V]\nfor: { each: [a], as: I }\nexec: [a]\ncapture: V\n", "RF-15 2:11")]
    [Arguments("outputs: [V]\nlog: x\n", "RF-23 2:11")]
    [Arguments("all:\n  - set: { A: x }\n  - set: { A: y }\n", "RF-8 4:12")]
    [Arguments("do:\n  - set: { A: x }\n  - set: { A: y }\n", "RF-26 4:12")]
    [Arguments("inputs: { A: { default: x } }\nany:\n  - set: { A: y }\n  - log: x\n", "RF-7 4:12")]
    [Arguments("do:\n  - set: { I: x }\n  - for: { each: [a], as: I }\n    log: x\n", "RF-7 4:27")]
    public async Task Scope_errors(string plan, string expected)
    {
        await Assert.That(Errors(plan)).IsEqualTo("BK_PLAN_INVALID " + expected);
    }

    [Test]
    [Arguments("timeout: { var: X }\nlog: x\n", "RF-9 2:10")]
    [Arguments("exec: [a, '--n=${X}']\n", "RF-9 2:11")]
    [Arguments("exec: [a, '$${X}']\n", "")]
    [Arguments("script: echo ${HOME}\n", "")]
    [Arguments("env: { T: { value: x, secret: true } }\nexec: [a]\n", "RF-19 2:23")]
    public async Task Literal_fields(string plan, string expected)
    {
        await Assert.That(Errors(plan)).IsEqualTo(expected.Length == 0 ? "" : "BK_PLAN_INVALID " + expected);
    }

    [Test]
    [Arguments("env: { PATH: x }\nexec: [a]\n", "RF-10 2:8")]
    [Arguments("vars: { LD_X: x }\nexport: [LD_X]\nexec: [a]\n", "RF-10 3:10")]
    [Arguments("vars: { M: x }\nenv: { M: y }\nexport: [M]\nexec: [a]\n", "RF-10 4:10")]
    [Arguments("services:\n  - name: db\n    exec: [db]\n    env: { BASH_ENV: x }\n    ready: { port: 1, open: true }\nexec: [a]\n",
        "RF-10 5:12")]
    [Arguments("vars: { M: x }\nexport: [M]\nexec: [a]\n", "")]
    public async Task Environment_names(string plan, string expected)
    {
        await Assert.That(Errors(plan)).IsEqualTo(expected.Length == 0 ? "" : "BK_PLAN_INVALID " + expected);
    }

    [Test]
    public async Task An_unset_from_env_in_vars_is_reported_at_its_name()
    {
        await Assert.That(Errors("vars:\n  A: { from_env: NOPE }\nlog: x\n"))
            .IsEqualTo($"{ErrorCodes.FromEnvUnset} NOPE 3:18");
        await Assert.That(Errors("vars:\n  A: { from_env: SET }\nlog: x\n", n => n == "SET" ? "" : null)).IsEqualTo("");
        await Assert.That(Errors("set: { A: { from_env: NOPE } }\n")).IsEqualTo("");
    }

    [Test]
    public async Task The_scope_check_is_linear_in_the_plan()
    {
        var plan = new StringBuilder("blokky: 1.0\ndo:\n");
        for (var i = 0; i < 20_000; i++)
        {
            plan.Append(CultureInfo.InvariantCulture,
                $"  - if: {{ var: V{i}, defined: true }}\n    then:\n      - set: {{ V{i + 1}: x }}\n");
        }

        var bytes = Encoding.UTF8.GetBytes(plan.ToString());
        var (result, _, _) = await Bounded.RunAsync(
            () => new PlanValidator(Everything, rules: [new VariableRules()]).Validate(bytes), TimeSpan.FromSeconds(20));

        await Assert.That(result.Errors.Count).IsEqualTo(1); // V0 is bound by nothing
    }
}
