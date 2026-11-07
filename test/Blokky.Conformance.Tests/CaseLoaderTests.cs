using System.Text;
using Blokky.Conformance.Cases;
using Blokky.Conformance.Yaml;

namespace Blokky.Conformance.Tests;

public class CaseLoaderTests
{
    private const string Header = "title: t\nrules: [EM-1]\nfeatures: [log]\nplan: |\n  blokky: 1.0\n  log: hi\n";

    internal static readonly string RunCase =
        Fixtures.Read("grace-run-case.yaml");

    [Test]
    public async Task A_run_case_is_read_with_its_unix_and_linux_overrides()
    {
        var c = CaseLoader.Parse("execution-model/grace", RunCase, "linux");
        var node = (OrderedDictionary<string, object?>)((OrderedDictionary<string, object?>)c.Expect["nodes"]!)["/all/0"]!;

        await Assert.That(c.ExpectKind).IsEqualTo(ExpectKind.Run);
        await Assert.That(Encoding.UTF8.GetString(c.Plan)).StartsWith("blokky: 1.0\nall:\n");
        await Assert.That(c.Inputs).IsEquivalentTo(["A=1", "B"]);
        await Assert.That(node["signal"]).IsEqualTo("SIGKILL");
        await Assert.That(node["status"]).IsEqualTo("timed_out");
        await Assert.That(YamlValues.Show(node["duration"])).IsEqualTo("{ $min: '7s', $max: '20s' }");
        await Assert.That(c.Env.ContainsKey("CI")).IsFalse();
        await Assert.That(c.Env["HOME"]).IsNull();
        await Assert.That(c.Files["bin.sh"].Executable).IsTrue();
        await Assert.That(c.Files["empty"].Content).IsNull();
        await Assert.That(c.Programs["git"]).IsEquivalentTo(["print", "--out", "git version 2.0"]);
        await Assert.That(c.PlatformSkip).IsNull();
    }

    [Test]
    public async Task A_platform_skip_is_recorded()
    {
        var c = CaseLoader.Parse("x/y", RunCase, "windows");

        await Assert.That(c.PlatformSkip).IsEqualTo("no signals");
    }

    [Test]
    public async Task A_validation_case_and_a_base64_plan()
    {
        var c = CaseLoader.Parse("syntax/x", Fixtures.Read("base64-validation-case.yaml"), "linux");

        await Assert.That(c.ExpectKind).IsEqualTo(ExpectKind.Validation);
        await Assert.That(Encoding.UTF8.GetString(c.Plan)).IsEqualTo("blokky: 1.0\r\nlog: hi\r\n");
    }

    [Test]
    [Arguments("color: red", "unknown top-level key 'color'")]
    [Arguments("plan_base64: eA==", "exactly one of plan and plan_base64")]
    [Arguments("features: [warp-drive]", "unknown feature id 'warp-drive'")]
    [Arguments("files: { ../x: a }", "isn't a relative path")]
    [Arguments("platforms: { amiga: { skip: x } }", "unknown platform key 'amiga'")]
    public async Task Broken_cases_are_rejected(string extra, string message)
    {
        var text = "title: t\nrules: [EM-1]\nplan: |\n  blokky: 1.0\n  log: hi\nexpect: { status: succeeded }\n" + extra + "\n";
        if (!extra.StartsWith("features", StringComparison.Ordinal))
        {
            text += "features: [log]\n";
        }

        var ex = Assert.Throws<CaseFormatException>(() => CaseLoader.Parse("a/b", text, "linux"));

        await Assert.That(ex.Message).Contains(message);
    }

    [Test]
    public async Task No_result_needs_a_kill_action()
    {
        const string Text =
            "title: t\nrules: [EM-22]\nfeatures: [exec]\nplan: |\n  blokky: 1.0\n  exec: [blokky-testkit, sleep, 1m]\n"
            + "expect: { no_result: true }\n";

        await Assert.That(() => CaseLoader.Parse("a/b", Text, "linux")).Throws<CaseFormatException>();
        var c = CaseLoader.Parse("a/b",
            Text + "actions: [{ after: 1s, kill: true }]\nafter_run: [{ argv: [alive, pids.txt], exit_code: 0 }]\n", "linux");
        await Assert.That(c.ExpectKind).IsEqualTo(ExpectKind.NoResult);
        await Assert.That(c.Actions[0]).IsEqualTo(new CaseAction(TimeSpan.FromSeconds(1), Kill: true));
        await Assert.That(c.AfterRun[0].Argv).IsEquivalentTo(["alive", "pids.txt"]);
    }

    [Test]
    public async Task An_outcome_mixing_validation_and_run_keys_is_broken()
    {
        const string Text = Header + "expect: { status: failed, plan_errors: [{ code: X }] }\n";

        await Assert.That(() => CaseLoader.Parse("a/b", Text, "linux")).Throws<CaseFormatException>();
    }

    [Test]
    public async Task A_bad_matcher_in_a_skipped_case_is_still_an_error()
    {
        const string Text = Header + "expect: { status: { $nope: 1 } }\n";

        await Assert.That(() => CaseLoader.Parse("a/b", Text, "linux")).Throws<CaseFormatException>();
    }

    [Test]
    public async Task The_id_is_the_path_below_cases_without_the_extension()
    {
        var dir = Path.Combine("x", "cases");

        var file = Path.Combine(dir, "reference", "for", "glob-order.yaml");

        await Assert.That(CaseLoader.IdOf(dir, file)).IsEqualTo("reference/for/glob-order");
    }
}
