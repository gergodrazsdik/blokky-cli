using Blokky.Conformance.Execution;
using Blokky.Conformance.Matching;

namespace Blokky.Conformance.Tests;

public class CaseRunnerTests
{
    private static readonly Matcher Matcher = new();

    private static AdapterOutcome Rejected(string json, int code = 3) => BlokkyAdapter.Interpret(AdapterTests.Exited(code, json));

    [Test]
    public async Task A_well_formed_not_supported_rejection_passes_the_check()
    {
        var (problem, note) = CaseRunner.NotSupportedProblem(Rejected(AdapterTests.NotSupportedEnvelope), 0, ["log"]);

        await Assert.That(problem).IsNull();
        await Assert.That(note).IsNull();
    }

    [Test]
    public async Task A_feature_the_case_does_not_list_is_noted_not_failed()
    {
        var (problem, note) = CaseRunner.NotSupportedProblem(Rejected(AdapterTests.NotSupportedEnvelope), 0, ["exec"]);

        await Assert.That(problem).IsNull();
        await Assert.That(note).Contains("'log'");
    }

    [Test]
    public async Task The_not_supported_check_fails_on_a_started_process_a_wrong_exit_or_code()
    {
        const string Yaml = """{"status":"invalid","phase":"plan","errors":[{"code":"BK_PLAN_YAML","message":"m"}]}""";
        const string Warp =
            """{"status":"invalid","phase":"plan","errors":[{"code":"BK_PLAN_NOT_SUPPORTED","message":"m","detail":{"feature":"warp"}}]}""";
        string? Problem(AdapterOutcome outcome, int log = 0) => CaseRunner.NotSupportedProblem(outcome, log, ["log"]).Problem;

        await Assert.That(Problem(Rejected(AdapterTests.NotSupportedEnvelope), log: 1)).Contains("a process started");
        await Assert.That(Problem(Rejected(AdapterTests.NotSupportedEnvelope, 2))).Contains("exit 2");
        await Assert.That(Problem(Rejected(Yaml))).Contains("no BK_PLAN_NOT_SUPPORTED");
        await Assert.That(Problem(Rejected(Warp))).Contains("not a feature id");
        await Assert.That(Problem(Rejected("""{"status":"succeeded","nodes":[]}""", 0))).Contains("Result");
    }

    [Test]
    public async Task Validation_errors_match_one_to_one_with_positions()
    {
        var outcome = Rejected(
            """
            {"status":"invalid","phase":"plan","errors":[
                {"code":"BK_PLAN_INVALID","message":"m","pos":"2:1","detail":{"rule":"RF-5"}},
                {"code":"BK_PLAN_UNKNOWN_KEY","message":"m","pos":"4:3"}]}
            """);
        string? Check(string expect, int log = 0) => CaseRunner.MatchValidation(Values.Map(expect), outcome, log, Matcher);

        await Assert.That(Check("plan_errors: [{ code: BK_PLAN_INVALID, detail: { rule: RF-5 }, line: 2, column: 1 }]")).IsNull();
        await Assert.That(Check("plan_errors: [{ code: BK_PLAN_INVALID, line: 3 }]")).IsNotNull();
        await Assert.That(Check("plan_errors: [{ code: BK_PLAN_INVALID }]\nexhaustive: true")).Contains("exhaustive");
        await Assert.That(Check("plan_errors: [{ code: BK_PLAN_INVALID }]", log: 2)).Contains("CF-23");
        await Assert.That(Check("plan_errors: [{ code: BK_PLAN_INVALID }, { code: BK_PLAN_INVALID }]")).IsNotNull();
    }

    [Test]
    public async Task A_run_outcome_needs_a_result_and_matches_the_invocation_log()
    {
        var result = BlokkyAdapter.Interpret(AdapterTests.Exited(0,
            """{"status":"succeeded","error":null,"nodes":[{"id":"/","kind":"exec","status":"succeeded"}]}"""));
        var log = (List<object?>)Values.Yaml("[{ name: blokky-testkit, argv: [exit, '0'], pid: 5 }]")!;
        var expect = Values.Map(
            "status: succeeded\nerror: null\nnodes: { /: { status: succeeded }, /do/0: { $absent: true } }\n"
            + "invocations: [{ argv: [exit, '0'] }]");
        var rejected = Rejected(AdapterTests.NotSupportedEnvelope);

        await Assert.That(CaseRunner.MatchRun(expect, result, log, "/nonexistent", Matcher)).IsNull();
        await Assert.That(CaseRunner.MatchRun(expect, result, [], "/nonexistent", Matcher)).StartsWith("invocations");
        await Assert.That(CaseRunner.MatchRun(expect, rejected, log, "/nonexistent", Matcher)).Contains("EM-3");
    }

    [Test]
    public async Task Files_are_checked_for_existence_text_and_contents()
    {
        var dir = Directory.CreateTempSubdirectory("conformance-files-").FullName;
        try
        {
            await File.WriteAllTextAsync(Path.Combine(dir, "out.txt"), "hello world\n");

            string? Check(string expect) => CaseRunner.MatchFiles(Values.Yaml(expect), dir, Matcher);

            await Assert.That(Check("{ out.txt: { contains: world }, gone.txt: { exists: false } }")).IsNull();
            await Assert.That(Check("{ out.txt: { text: \"hello world\\n\" } }")).IsNull();
            await Assert.That(Check("{ out.txt: { text: hello } }")).IsNotNull();
            await Assert.That(Check("{ out.txt: { exists: false } }")).IsNotNull();
            await Assert.That(Check("{ missing.txt: { contains: x } }")).IsNotNull();
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}

/// <summary>The whole run path with a fake blokky (canned report) and <c>true</c> standing in for the testkit.</summary>
public class CaseRunnerEndToEndTests
{
    private static readonly string Case =
        Fixtures.Read("e2e-case.yaml");

    [Test]
    public async Task A_supported_case_runs_and_passes()
    {
        if (OperatingSystem.IsWindows() || !File.Exists("/usr/bin/true"))
        {
            Skip.Test("needs /usr/bin/true");
        }

        var root = Directory.CreateTempSubdirectory("conformance-e2e-").FullName;
        try
        {
            var cases = Path.Combine(root, "cases", "area");
            Directory.CreateDirectory(cases);
            var file = Path.Combine(cases, "one.yaml");
            await File.WriteAllTextAsync(file, Case);
            var fake = new FakeRunner(AdapterTests.Exited(0,
                """{"status":"succeeded","error":null,"nodes":[{"id":"/","kind":"exec","status":"succeeded","exit_code":0}]}"""));
            var caps = new AdapterCapabilities(new HashSet<string> { "exec" }, ["real"], true, false);
            var host = new HostInfo("linux", new Dictionary<string, string>(), new Dictionary<string, string>(), "/usr/bin/true", false);
            var settings = new RunnerSettings(Path.Combine(root, "run"), 1, 10, false);
            var runner = new CaseRunner(new BlokkyAdapter("/fake/blokky", fake, caps), host, settings);

            var result = await runner.RunAsync(Path.Combine(root, "cases"), file, CancellationToken.None);

            await Assert.That(result.Reason).IsNull();
            await Assert.That(result.Outcome).IsEqualTo(Outcome.Pass);
            await Assert.That(result.Clock).IsEqualTo("real");
            await Assert.That(fake.Last!.Env["PATH"]).StartsWith(Path.Combine(root, "run", "area.one", "bin"));
            await Assert.That(fake.Last.Env["TESTKIT_TIME_SCALE"]).IsEqualTo("1");
            await Assert.That(fake.Last.Deadline).IsEqualTo(TimeSpan.FromSeconds(60));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
