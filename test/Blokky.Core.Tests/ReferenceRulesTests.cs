using System.Text;
using Blokky.Core.Validation;
using Blokky.Core.Validation.Rules.Reference;

namespace Blokky.Core.Tests;

/// <summary>The reference and modifiers rule family (T-0085): each rule alone, through only these rule classes.</summary>
public class ReferenceRulesTests
{
    private static readonly IPlanRule[] Family =
    [
        new ValidityMatrixRule(), new LoopRule(), new NestingRule(), new ModifierValueRule(), new InputConstraintRule(),
        new PwshScriptRule(),
    ];

    /// <summary>Each error as <c>code rule line:column node</c>, the not-supported gate left out.</summary>
    private static string Errors(string plan) =>
        string.Join("; ", new PlanValidator(new HashSet<string>(), rules: Family).Validate(Encoding.UTF8.GetBytes(plan)).Errors
            .Where(e => e.Code != "BK_PLAN_NOT_SUPPORTED")
            .Select(e => $"{e.Code} {e.Detail?["rule"]} {e.Pos} {e.Node}"));

    [Test]
    [Arguments("blokky: 1.0\ndo:\n  - sleep: 1s\n    timeout: 5s\n", "BK_PLAN_INVALID RF-28 4:5 /do/0")]
    [Arguments("blokky: 1.0\ndo:\n  - log: x\n    finally:\n      - log: y\n    finally_timeout: 1m\n",
        "BK_PLAN_INVALID RF-28 4:5 /do/0; BK_PLAN_INVALID RF-28 6:5 /do/0")]
    [Arguments("blokky: 1.0\nfinally_timeout: 1m\nexec: [a]\n", "BK_PLAN_INVALID RF-32 2:1 /")]
    [Arguments("blokky: 1.0\nexec: [a]\nshell: bash\n", "BK_PLAN_INVALID RF-32 3:1 /")]
    [Arguments("blokky: 1.0\nif: { var: X, equals: a }\nelse:\n  - log: b\n", "BK_PLAN_INVALID RF-32 2:1 /")]
    [Arguments("blokky: 1.0\nignore_errors: true\nexec: [a]\n", "BK_PLAN_INVALID RF-33 2:1 /")]
    [Arguments("blokky: 1.0\ncapture: OUT\ndo:\n  - exec: [a]\n", "BK_PLAN_INVALID RF-12 2:1 /")]
    [Arguments("blokky: 1.0\ndo:\n  - log: x\n    cwd: sub\n", "BK_PLAN_INVALID RF-3 4:5 /do/0")]
    [Arguments("blokky: 1.0\ndo:\n  - do:\n      - exec: [a]\n    ok_exit: [1]\n", "BK_PLAN_INVALID RF-3 5:5 /do/0")]
    [Arguments("blokky: 1.0\nexec: [a]\nconcurrency: 2\n", "BK_PLAN_INVALID RF-3 3:1 /")]
    [Arguments("blokky: 1.0\nall:\n  - exec: [a]\nconcurrency: 2\nfail_fast: false\n", "")]
    [Arguments("blokky: 1.0\nfor: { each: [a], as: I }\nconcurrency: 2\nall:\n  - exec: [a]\n", "BK_PLAN_INVALID RF-31 3:1 /")]
    [Arguments("blokky: 1.0\nfor: { each: [a], as: I, concurrency: 2, fail_fast: false }\nexec: [a]\n", "")]
    public async Task The_validity_matrix_and_its_specific_rules(string plan, string expected)
    {
        await Assert.That(Errors(plan)).IsEqualTo(expected);
    }

    [Test]
    [Arguments("blokky: 1.0\nfor: { each: [a], as: I }\nretry: true\nexec: [a]\n", "BK_PLAN_INVALID RF-2 3:1 /")]
    [Arguments("blokky: 1.0\nattempt_timeout: 1m\nexec: [a]\n", "BK_PLAN_INVALID RF-4 2:1 /")]
    [Arguments("blokky: 1.0\nretry: true\nattempt_timeout: 1m\nmax: 2\nevery: 0s\nbackoff: { factor: 2 }\njitter: 0.5\nexec: [a]\n", "")]
    [Arguments("blokky: 1.0\ndo:\n  - log: x\n    for: { each: [a], as: I }\n    attempt_timeout: 1m\n", "BK_PLAN_INVALID RF-3 5:5 /do/0")]
    [Arguments("blokky: 1.0\nuntil: { var: X, equals: a }\nexec: [a]\ncapture: X\n", "BK_PLAN_INVALID RF-5 2:1 /")]
    [Arguments("blokky: 1.0\nwhile: { var: X, equals: a }\ntimeout: 1m\nexec: [a]\n", "")]
    [Arguments("blokky: 1.0\nfor: { each: [a], as: I }\nmax: 2\nexec: [a]\n", "BK_PLAN_INVALID RF-3 3:1 /")]
    [Arguments("blokky: 1.0\nwhile: { var: X, equals: a }\nmax: 2\njitter: 0.5\nexec: [a]\n", "BK_PLAN_INVALID RF-3 4:1 /")]
    [Arguments("blokky: 1.0\nfor: { each: [a], as: I }\nindex: N\nexec: [a]\n", "BK_PLAN_INVALID RF-30 3:1 /")]
    [Arguments("blokky: 1.0\nretry: true\ncarry: { P: OUT }\nexec: [a]\ncapture: OUT\n", "BK_PLAN_INVALID RF-30 3:1 /")]
    public async Task Repetition_and_its_sibling_fields(string plan, string expected)
    {
        await Assert.That(Errors(plan)).IsEqualTo(expected);
    }

    [Test]
    [Arguments("blokky: 1.0\nuntil: { var: N, empty: true }\ncarry: { C: NEXTT }\nmax: 3\ndo:\n  - exec: [a]\n    capture: N\n",
        "BK_PLAN_INVALID RF-30 3:13 /")]
    [Arguments("blokky: 1.0\nfor: { each: [a], as: I }\ncollect: { OUTT: ALL }\nexec: [a]\ncapture: OUT\n",
        "BK_PLAN_INVALID RF-30 3:12 /")]
    [Arguments("blokky: 1.0\nfor: { each: [a], as: I }\ncollect: { OUT: ALL }\ndo:\n  - set: { OUT: x }\n", "")]
    [Arguments("blokky: 1.0\ndo:\n  - log: x\n    for: { each: [a], as: I }\n    collect: { OUT: ALL }\n",
        "BK_PLAN_INVALID RF-30 5:5 /do/0")]
    [Arguments("blokky: 1.0\nfor: { each: [a] }\nexec: [a]\n", "BK_PLAN_INVALID RF-18 2:1 /")]
    [Arguments("blokky: 1.0\nfor: { matrix: { OS: [linux] }, as: M }\nexec: [a]\n", "BK_PLAN_INVALID RF-18 2:33 /")]
    [Arguments("blokky: 1.0\nfor: { each: [a], as: I, allow_empty: true, keep_blank: true }\nexec: [a]\n",
        "BK_PLAN_INVALID RF-25 2:45 /; BK_PLAN_INVALID RF-25 2:26 /")]
    public async Task Loop_sources_and_loop_variables(string plan, string expected)
    {
        await Assert.That(Errors(plan)).IsEqualTo(expected);
    }

    [Test]
    [Arguments("blokky: 1.0\nlock: a\ndo:\n  - lock: b\n    exec: [a]\n", "BK_PLAN_INVALID RF-17 4:5 /do/0")]
    [Arguments("blokky: 1.0\nlock: a\nexec: [a]\nfinally:\n  - lock: b\n    exec: [a]\n", "BK_PLAN_INVALID RF-17 5:5 /finally/0")]
    [Arguments("blokky: 1.0\ndo:\n  - lock: a\n    exec: [a]\n  - lock: a\n    exec: [a]\n", "")]
    [Arguments("blokky: 1.0\ncontainer: { image: busybox }\ndo:\n  - container: { image: busybox }\n    exec: [a]\n",
        "BK_PLAN_INVALID CP-19 4:5 /do/0")]
    [Arguments("blokky: 1.0\nreturn: done\n", "BK_PLAN_INVALID RF-29 2:1 /")]
    [Arguments("blokky: 1.0\nall:\n  - return: done\n", "BK_PLAN_INVALID RF-29 3:5 /all/0")]
    [Arguments("blokky: 1.0\nif: { var: X, equals: a }\nthen:\n  - return: done\n", "BK_PLAN_INVALID RF-29 4:5 /then/0")]
    [Arguments("blokky: 1.0\ndo:\n  - any:\n      - if: { var: X, equals: a }\n        then:\n          - return: q\n",
        "BK_PLAN_INVALID RF-29 6:13 /do/0/any/0/then/0")]
    [Arguments("blokky: 1.0\ndo:\n  - if: { var: X, equals: a }\n    then:\n      - return: q\n"
        + "  - all:\n      - do:\n          - return: r\n", "")]
    [Arguments("blokky: 1.0\nexec: [a]\nfinally:\n  - return: done\n", "")]
    public async Task Nesting_lock_container_and_return(string plan, string expected)
    {
        await Assert.That(Errors(plan)).IsEqualTo(expected);
    }

    [Test]
    [Arguments("blokky: 1.0\ntempdir: true\ncwd: /tmp\nexec: [a]\n", "BK_PLAN_INVALID RF-11 3:1 /")]
    [Arguments("blokky: 1.0\ntempdir: true\ncwd: sub\nexec: [a]\n", "")]
    [Arguments("blokky: 1.0\nservices:\n  - exec: [a]\n    ready: { port: 1, open: true }\nexec: [a]\n", "BK_PLAN_INVALID RF-24 3:5 /")]
    [Arguments("blokky: 1.0\nservices:\n  - { name: a, exec: [a], ready: { port: 1, open: true } }\n"
        + "  - { name: a, exec: [a], ready: { port: 1, open: true } }\nexec: [a]\n", "BK_PLAN_INVALID RF-24 4:13 /")]
    [Arguments("blokky: 1.0\ncontainer: { image: busybox, mounts: [{ host: ., path: src }] }\nexec: [a]\n",
        "BK_PLAN_VALUE CP-13 2:56 /")]
    [Arguments("blokky: 1.0\ninputs:\n  E: { default: prod, in: [staging, production] }\nlog: x\n", "BK_PLAN_INVALID RF-20 3:17 /")]
    [Arguments("blokky: 1.0\ninputs:\n  E: { in: [a], matches: '^a$' }\nlog: x\n", "BK_PLAN_INVALID RF-20 3:17 /")]
    public async Task Modifier_values_and_inputs(string plan, string expected)
    {
        if (OperatingSystem.IsWindows() && plan.Contains("/tmp", StringComparison.Ordinal))
        {
            Skip.Test("/tmp isn't an absolute host path on Windows (CP-41)");
            return;
        }

        await Assert.That(Errors(plan)).IsEqualTo(expected);
    }

    [Test]
    [Arguments("param($Name)\nWrite-Output $Name\n", true)]
    [Arguments("# a comment\n[CmdletBinding()]\nparam()\n", true)]
    [Arguments("param\n(\n  $Name\n)\n", true)]
    [Arguments("[CmdletBinding()] [OutputType([string])] Param()\n", true)]
    [Arguments("<# block\n comment #>\nusing namespace System.IO\nEND { 'a' }\n", true)]
    [Arguments("dynamicparam { }\nend { 'a' }\n", true)]
    [Arguments("[Console]::Out.Write('a')\nparam()\n", false)]
    [Arguments("Write-Output a\nparam($x)\n", false)]
    [Arguments("$params = 1\n", false)]
    [Arguments("ending\n", false)]
    [Arguments("# only a comment\n<# and a block #>\n", false)]
    public async Task The_pwsh_prologue_line_check(string script, bool rejected)
    {
        await Assert.That(PwshScriptRule.Rejects(script)).IsEqualTo(rejected);
    }

    [Test]
    [Arguments("blokky: 1.0\nshell: pwsh\nscript: |\n  param($A)\n", "BK_PLAN_INVALID SH-17 4:3 /")]
    [Arguments("blokky: 1.0\ndefaults: { shell: powershell }\nscript: |\n  begin { }\n", "BK_PLAN_INVALID SH-17 4:3 /")]
    [Arguments("blokky: 1.0\nscript: |\n  param($A)\n", "")]
    [Arguments("blokky: 1.0\ndefaults: { shell: pwsh }\nshell: bash\nscript: |\n  param($A)\n", "")]
    public async Task Pwsh_scripts_with_a_named_shell(string plan, string expected)
    {
        await Assert.That(Errors(plan)).IsEqualTo(expected);
    }
}
