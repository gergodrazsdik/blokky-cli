using System.Collections.Frozen;
using System.Text;
using Blokky.Core.Ast;
using Blokky.Core.Errors;
using Blokky.Core.Features;
using Blokky.Core.Reading;
using Blokky.Core.Validation;

namespace Blokky.Core.Tests;

public class AstBuilderTests
{
    private static readonly FrozenSet<string> Everything = FeatureIds.All.ToFrozenSet(StringComparer.Ordinal);

    private static ValidationResult Validate(string plan) =>
        new PlanValidator(Everything, rules: []).Validate(Encoding.UTF8.GetBytes(plan));

    /// <summary>Each error as <c>code rule-or-suggestion line:column node</c>.</summary>
    private static string Errors(string plan) =>
        string.Join("; ", Validate(plan).Errors.Select(e =>
            $"{e.Code} {e.Detail?["rule"] ?? e.Detail?["suggestion"] ?? "-"} {e.Pos} {e.Node}"));

    [Test]
    public async Task A_valid_plan_builds_a_typed_ast_with_path_ids_and_positions()
    {
        var result = Validate(
            "blokky: 1.0\nname: build\ntimeout: 10m\ndo:\n  - exec: [git, status]\n    retry: true\n    max: 3\n"
            + "  - if: { var: X, equals: a }\n    then:\n      - log: yes ${X}\n        level: warn\n");
        var ast = result.Ast!;
        var nodes = PlanWalk.Nodes(ast).ToList();

        await Assert.That(result.Errors.Count).IsEqualTo(0);
        await Assert.That(string.Join(" ", nodes.Select(n => $"{n.Path}={n.Kind}"))).IsEqualTo("/=Do /do/0=Exec /do/1=If /do/1/then/0=Log");
        await Assert.That(ast.Root.Timeout!.Value).IsEqualTo(TimeSpan.FromMinutes(10));
        await Assert.That(ast.Root.Timeout.Position).IsEqualTo(new PlanPosition(3, 10));
        await Assert.That(nodes[1].Position).IsEqualTo(new PlanPosition(5, 5));
        await Assert.That(nodes[1].Max!.Value).IsEqualTo(3L);
        await Assert.That(nodes[1].Retry!.Condition).IsNull();
        await Assert.That(string.Join(",", nodes[1].Exec!.Argv!.Select(a => ((LiteralValue)a!).Scalar.Text))).IsEqualTo("git,status");
        await Assert.That(nodes[2].If!.Subjects.Single().Kind).IsEqualTo(SubjectKind.Var);
        await Assert.That(nodes[2].If!.Operators.Single().Kind).IsEqualTo(OperatorKind.EqualsTo);
        await Assert.That(nodes[3].Level!.Value).IsEqualTo(LogLevel.Warn);
        await Assert.That(nodes[3].Log!.Form).IsEqualTo(FieldForm.Display);
        await Assert.That(nodes[3].KindKey!.Position).IsEqualTo(new PlanPosition(10, 9));
    }

    [Test]
    [Arguments("blokky: 1.0\ntimeout:\nexec: [a]\n", "BK_PLAN_VALUE SY-20 2:1 /")]
    [Arguments("blokky: 1.0\ndo:\n  - log: first\n  -\n", "BK_PLAN_VALUE SY-20 4:3 /")]
    [Arguments("blokky: 1.0\ndo:\n  -   # nothing\n  - log: x\n", "BK_PLAN_VALUE SY-20 3:3 /")]
    [Arguments("blokky: 1.0\ninputs:\n  BRANCH:\nlog: x\n", "BK_PLAN_VALUE SY-20 3:3 /")]
    [Arguments("blokky: 1.0\noutputs: []\ndo: []\n", "BK_PLAN_VALUE RF-21 2:10 /; BK_PLAN_VALUE RF-21 3:5 /")]
    [Arguments("blokky: 1.0\nenv: {}\nexec: [a]\n", "BK_PLAN_VALUE SY-22 2:6 /")]
    [Arguments("blokky: 1.0\ncontainer: { image: a, mounts: [] }\nexec: [a]\n", "")]
    [Arguments("blokky: 1.0\nscript: ''\n", "BK_PLAN_VALUE SY-20 2:9 /")]
    public async Task Empty_values_lists_and_mappings(string plan, string expected)
    {
        await Assert.That(Errors(plan)).IsEqualTo(expected);
    }

    [Test]
    [Arguments("blokky: 1.0\ndo:\n  - name: nothing\n", "BK_PLAN_INVALID RF-1 3:5 /do/0")]
    [Arguments("blokky: 1.0\ndo:\n  - { log: a, sleep: 1s }\n", "BK_PLAN_INVALID RF-1 3:15 /do/0")]
    [Arguments("blokky: 1.0\nexec: [a]\nif: { var: X, equals: a }\n", "BK_PLAN_INVALID RF-1 3:1 /")]
    [Arguments("blokky: 1.0\ndo:\n  - outputs: [A]\n    log: x\n", "BK_PLAN_INVALID SY-33 3:5 /do/0")]
    [Arguments("blokky: 1.0\ndo:\n  - blokky: 1.0\n    log: x\n", "BK_PLAN_INVALID SY-33 3:5 /do/0")]
    public async Task Kind_detection_and_plan_only_keys(string plan, string expected)
    {
        await Assert.That(Errors(plan)).IsEqualTo(expected);
    }

    [Test]
    public async Task Two_kinds_keep_the_first_and_check_both_values()
    {
        var result = Validate("blokky: 1.0\nlog: a\nsleep: 1x\n");

        await Assert.That(result.Ast!.Root.Kind).IsEqualTo(NodeKind.Log);
        await Assert.That(string.Join("; ", result.Errors.Select(e => $"{e.Detail!["rule"]} {e.Pos}"))).IsEqualTo("SY-56 3:8; RF-1 3:1");
        await Assert.That(result.Errors[1].Message).Contains("split them");
    }

    [Test]
    [Arguments("blokky: 1.0\ninputs: { A: x }\nlog: x\n", "inputs.A: takes a mapping such as { description: … } or { default: … }")]
    [Arguments("blokky: 1.0\nwhen: { probe: x }\nlog: x\n", "probe: takes a mapping with exec or script")]
    [Arguments("blokky: 1.0\nenv: {}\nlog: x\n", "env: an empty mapping isn't allowed: it takes a mapping of names to values")]
    public async Task Shape_messages_read_as_sentences(string plan, string message)
    {
        await Assert.That(Validate(plan).Errors.Select(e => e.Message)).Contains(message);
    }

    [Test]
    [Arguments("log: hello\n", "BK_PLAN_VERSION SY-34 1:1 /")]
    [Arguments("# head\n\nlog: hello\nblokky:\n", "BK_PLAN_VERSION SY-34 4:1 /")]
    [Arguments("blokky: 1.0.0\nlog: hello\n", "BK_PLAN_VERSION SY-51 1:9 /")]
    [Arguments("blokky: 99.0\nlog: hello\n", "BK_PLAN_VERSION SY-35 1:9 /")]
    [Arguments("blokky: [1.0]\nlog: hello\n", "BK_PLAN_VERSION SY-51 1:9 /")]
    [Arguments("{}\n", "BK_PLAN_VALUE SY-22 1:1 /")]
    public async Task Version_errors_list_the_supported_versions(string plan, string expected)
    {
        var result = Validate(plan);

        await Assert.That(Errors(plan)).IsEqualTo(expected);
        if (result.Errors[0].Code == ErrorCodes.PlanVersion)
        {
            await Assert.That(result.Errors[0].Detail!["supported"]!.ToJsonString()).IsEqualTo("[\"1.0\"]");
        }
    }

    [Test]
    [Arguments("blokky: 1.0\ntimeoutt: 5s\nexec: [a]\n", "BK_PLAN_UNKNOWN_KEY timeout 2:1 /")]
    [Arguments("blokky: 1.0\nlo: x\nexec: [a]\n", "BK_PLAN_UNKNOWN_KEY do 2:1 /")]
    [Arguments("blokky: 1.0\nxyzzy: x\nexec: [a]\n", "BK_PLAN_UNKNOWN_KEY - 2:1 /")]
    [Arguments("blokky: 1.0\nwhen: { probe: { exec: a 0,1 } }\nexec: [a]\n", "BK_PLAN_UNKNOWN_KEY - 2:28 /")]
    [Arguments("blokky: 1.0\ndefaults: { shel: sh }\nexec: [a]\n", "BK_PLAN_UNKNOWN_KEY shell 2:13 /")]
    [Arguments("blokky: 1.0\nenv: { A: { var: X, default: y } }\nexec: [a]\n", "BK_PLAN_UNKNOWN_KEY - 2:21 /")]
    public async Task Unknown_keys_with_suggestions(string plan, string expected)
    {
        await Assert.That(Errors(plan)).IsEqualTo(expected);
    }

    [Test]
    public async Task A_secret_input_is_an_unknown_key_with_a_targeted_fix_and_no_rule()
    {
        var error = Validate("blokky: 1.0\ninputs:\n  T:\n    description: x\n    secret: true\nlog: x\n").Errors.Single();

        await Assert.That(error.Code).IsEqualTo(ErrorCodes.PlanUnknownKey);
        await Assert.That(error.Detail).IsNull();
        await Assert.That(error.Fix).Contains("from_env");
    }

    [Test]
    [Arguments("blokky: 1.0\nname: [a, b]\nexec: [a]\n", "BK_PLAN_VALUE SY-88 2:7 /")]
    [Arguments("blokky: 1.0\ndo: { exec: [a] }\n", "BK_PLAN_VALUE SY-88 2:5 /")]
    [Arguments("blokky: 1.0\ndo:\n  - exec: { cmd: a, timeout: 5m }\n", "BK_PLAN_VALUE SY-88 3:11 /do/0")]
    [Arguments("blokky: 1.0\nshell: sh\nscript: [ -f x ]\n", "BK_PLAN_VALUE SY-89 3:9 /")]
    [Arguments("blokky: 1.0\nshell: sh\nscript:\n  - x\n", "BK_PLAN_VALUE SY-88 4:3 /")]
    [Arguments("blokky: 1.0\nretry: 3\nexec: [a]\n", "BK_PLAN_VALUE SY-88 2:8 /")]
    [Arguments("blokky: 1.0\nretry: false\nexec: [a]\n", "BK_PLAN_VALUE SY-53 2:8 /")]
    [Arguments("blokky: 1.0\nif: \"$X == 1\"\nthen: [{ log: x }]\n", "BK_PLAN_VALUE CO-4 2:5 /")]
    [Arguments("blokky: 1.0\nif: { var: X, equals: a }\nthen: { var: X, equals: b }\n", "BK_PLAN_VALUE CO-4 3:7 /")]
    [Arguments("blokky: 1.0\ninputs:\n  B:\n    default: { from_env: G }\nlog: x\n", "BK_PLAN_VALUE SY-47 4:14 /")]
    [Arguments("blokky: 1.0\nlevel: loud\nlog: x\n", "BK_PLAN_VALUE SY-88 2:8 /")]
    [Arguments("blokky: 1.0\nkeep_descendants: yes\nexec: [a]\n", "BK_PLAN_VALUE SY-53 2:19 /")]
    [Arguments("blokky: 1.0\nwhen: { var: X, gt: '1e3' }\nlog: x\n", "BK_PLAN_VALUE CO-19 2:21 /")]
    [Arguments("blokky: 1.0\nwhen: { command: x, exists: false }\nlog: x\n", "BK_PLAN_VALUE SY-53 2:29 /")]
    [Arguments("blokky: 1.0\nok_exit: [4294967296]\nexec: [a]\n", "BK_PLAN_VALUE SY-96 2:11 /")]
    [Arguments("blokky: 1.0\ncapture: run_id\nexec: [a]\n", "BK_PLAN_VALUE SY-52 2:10 /")]
    [Arguments("blokky: 1.0\nenv: { 9LIVES: cat }\nexec: [a]\n", "BK_PLAN_VALUE SY-52 2:8 /")]
    [Arguments("blokky: 1.0\ncode: BK_X\nfail: x\n", "BK_PLAN_VALUE SY-52 2:7 /")]
    [Arguments("blokky: 1.0\nlock: { wait: 1s }\nexec: [a]\n", "BK_PLAN_INVALID SY-88 2:7 /")]
    public async Task Field_types_and_grammars(string plan, string expected)
    {
        await Assert.That(Errors(plan)).IsEqualTo(expected);
    }

    [Test]
    [Arguments("blokky: 1.0\ndo:\n  - container: { mounts: [] }\n    exec: [a]\n", "BK_PLAN_INVALID CP-40 3:16 /do/0")]
    [Arguments("blokky: 1.0\ncontainer:\n  image: a\n  mounts:\n    - { host: . }\nexec: [a]\n", "BK_PLAN_INVALID CP-40 5:7 /")]
    public async Task Required_container_keys(string plan, string expected)
    {
        await Assert.That(Errors(plan)).IsEqualTo(expected);
    }

    [Test]
    public async Task References_in_literal_fields_and_secret_literals_are_recorded_not_reported()
    {
        var result = Validate("blokky: 1.0\ntimeout: { var: T }\nexec: [{ var: P }, x]\nenv: { A: { value: s, secret: true } }\n"
            + "cwd: { from_env: HOME }\n");
        var misplaced = result.Ast!.MisplacedReferences.Select(m => $"{m.Field}@{m.Value.Position}:{m.Value.GetType().Name}");

        await Assert.That(result.Errors.Count).IsEqualTo(0);
        await Assert.That(string.Join(" ", misplaced))
            .IsEqualTo("timeout@2:10:ReferenceValue exec@3:8:ReferenceValue env@4:11:SecretLiteralValue cwd@5:6:ReferenceValue");
    }

    [Test]
    public async Task A_node_used_as_a_condition_is_recorded_for_the_conditions_rules()
    {
        var result = Validate("blokky: 1.0\nwhen: { exec: [a], timeout: 1s }\nlog: x\n");

        await Assert.That(result.Errors.Count).IsEqualTo(0);
        await Assert.That(result.Ast!.Root.When!.NodeKindKey!.Position).IsEqualTo(new PlanPosition(2, 9));
    }

    [Test]
    public async Task A_wrong_value_is_left_out_and_the_rest_is_built()
    {
        var result = Validate("blokky: 1.0\ntimeout: 30\nname: x\nexec: [a]\n");

        await Assert.That(result.Errors.Count).IsEqualTo(1);
        await Assert.That(result.Ast!.Root.Timeout).IsNull();
        await Assert.That(result.Ast.Root.Name!.Text).IsEqualTo("x");
        await Assert.That(result.Ast.Root.Has("timeout")).IsTrue();
    }

    [Test]
    public async Task Recoverable_reader_errors_are_reported_with_the_later_ones()
    {
        var result = Validate("blokky: 1.0\nname: &a x\nlog: *a\nlog: y\ntimout: 1s\n");
        var errors = result.Errors.Select(e => $"{e.Code} {e.Pos}");

        await Assert.That(string.Join("; ", errors))
            .IsEqualTo("BK_PLAN_YAML 2:7; BK_PLAN_YAML 3:6; BK_PLAN_DUPLICATE_KEY 4:1; BK_PLAN_UNKNOWN_KEY 5:1");
        await Assert.That(result.Ast!.Root.Kind).IsEqualTo(NodeKind.Log);
    }

    [Test]
    [Arguments("blokky: 1.0\nlog: [\n")]
    [Arguments("blokky: 1.0\n: x\nlog: y\n")]
    [Arguments("- log: x\n")]
    [Arguments("")]
    public async Task Fatal_reader_errors_stop_before_the_ast(string plan)
    {
        var result = Validate(plan);

        await Assert.That(result.Ast).IsNull();
        await Assert.That(result.Errors.Count).IsGreaterThan(0);
        await Assert.That(result.Read.CanContinue).IsFalse();
    }

    [Test]
    public async Task A_large_plan_validates_in_linear_time()
    {
        var sb = new StringBuilder("blokky: 1.0\ndo:\n");
        for (var i = 0; i < 20_000; i++)
        {
            sb.Append("  - name: step ${X}\n    timeout: 5m\n    when: { var: X, in: [a, b] }\n    exec: [prog, --flag, { var: X }]\n");
        }

        var clock = System.Diagnostics.Stopwatch.StartNew();
        var result = Validate(sb.ToString());

        await Assert.That(result.Errors.Count).IsEqualTo(0);
        await Assert.That(PlanWalk.Nodes(result.Ast!).Count()).IsEqualTo(20_001);
        await Assert.That(clock.Elapsed).IsLessThan(TimeSpan.FromSeconds(20));
    }
}
