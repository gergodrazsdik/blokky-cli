using Blokky.Conformance.Cases;
using Blokky.Conformance.Execution;
using Blokky.Conformance.Matching;

namespace Blokky.Conformance.Tests;

public class CaseRunnerTests
{
    private static readonly Matcher Matcher = new();

    private static readonly HashSet<string> None = [];

    private static AdapterOutcome Rejected(string json, int code = 3) => BlokkyAdapter.Interpret(AdapterTests.Exited(code, json));

    [Test]
    public async Task A_well_formed_not_supported_rejection_passes_the_check()
    {
        var (problem, note) = CaseRunner.NotSupportedProblem(Rejected(AdapterTests.NotSupportedEnvelope), 0, ["log"], None);

        await Assert.That(problem).IsNull();
        await Assert.That(note).IsNull();
    }

    [Test]
    public async Task The_check_fails_when_the_named_feature_is_declared_for_the_case()
    {
        var problem = CaseRunner.NotSupportedProblem(Rejected(AdapterTests.NotSupportedEnvelope), 0, ["log"],
            new HashSet<string> { "log" }).Problem;

        await Assert.That(problem).Contains("declares supported");
    }

    [Test]
    public async Task A_feature_the_case_does_not_list_is_noted_not_failed()
    {
        var (problem, note) = CaseRunner.NotSupportedProblem(Rejected(AdapterTests.NotSupportedEnvelope), 0, ["exec"], None);

        await Assert.That(problem).IsNull();
        await Assert.That(note).Contains("'log'");
    }

    [Test]
    public async Task The_not_supported_check_fails_on_a_started_process_a_wrong_exit_or_code()
    {
        const string Yaml = """{"status":"invalid","phase":"plan","errors":[{"code":"BK_PLAN_YAML","message":"m"}]}""";
        const string Warp =
            """{"status":"invalid","phase":"plan","errors":[{"code":"BK_PLAN_NOT_SUPPORTED","message":"m","detail":{"feature":"warp"}}]}""";
        string? Problem(AdapterOutcome outcome, int log = 0) => CaseRunner.NotSupportedProblem(outcome, log, ["log"], None).Problem;

        await Assert.That(Problem(Rejected(AdapterTests.NotSupportedEnvelope), log: 1)).Contains("a process started");
        await Assert.That(Problem(Rejected(AdapterTests.NotSupportedEnvelope, 2))).Contains("exit 2");
        await Assert.That(Problem(Rejected(Yaml))).Contains("no BK_PLAN_NOT_SUPPORTED");
        await Assert.That(Problem(Rejected(Warp))).Contains("not a feature id");
        await Assert.That(Problem(Rejected("""{"status":"succeeded","nodes":[]}""", 0))).Contains("Result");
    }

    [Test]
    public async Task A_process_that_outlived_blokky_fails_the_no_process_checks()
    {
        var notSupported = Rejected(AdapterTests.NotSupportedEnvelope);
        var yaml = Rejected("""{"status":"invalid","phase":"plan","errors":[{"code":"BK_PLAN_YAML","message":"m"}]}""");

        await Assert.That(CaseRunner.NotSupportedProblem(notSupported, 0, ["log"], None, survivors: 1).Problem).Contains("outlived blokky");
        await Assert.That(CaseRunner.MatchValidation(Values.Map("plan_errors: [{ code: BK_PLAN_YAML }]"), yaml, 0, Matcher, survivors: 2))
            .Contains("CF-23");
    }

    [Test]
    public async Task The_own_errors_fallback_needs_a_plan_rejection_with_exit_3()
    {
        const string Yaml = """{"status":"invalid","phase":"plan","errors":[{"code":"BK_PLAN_YAML","message":"m"}]}""";
        const string UsageYaml = """{"status":"invalid","phase":"usage","errors":[{"code":"BK_PLAN_YAML","message":"m"}]}""";
        var expect = Values.Map("plan_errors: [{ code: BK_PLAN_YAML }]");
        bool Fallback(AdapterOutcome outcome, string? expectYaml = null, int log = 0, int survivors = 0) =>
            CaseRunner.RejectedWithOwnErrors(ExpectKind.Validation, expectYaml is null ? expect : Values.Map(expectYaml), outcome, log,
                survivors, Matcher);

        await Assert.That(Fallback(Rejected(Yaml))).IsTrue();
        await Assert.That(Fallback(Rejected(Yaml, 2))).IsFalse();
        await Assert.That(Fallback(Rejected(Yaml, 0))).IsFalse();
        await Assert.That(Fallback(Rejected(UsageYaml))).IsFalse();
        await Assert.That(Fallback(Rejected(Yaml), log: 1)).IsFalse();
        await Assert.That(Fallback(Rejected(Yaml), survivors: 1)).IsFalse();
        await Assert.That(Fallback(Rejected(Yaml), "input_errors: [{ code: BK_INPUT_SYNTAX }]")).IsFalse();
        await Assert.That(CaseRunner.RejectedWithOwnErrors(ExpectKind.Run, expect, Rejected(Yaml), 0, 0, Matcher)).IsFalse();
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
            var caps = new AdapterCapabilities(new HashSet<string> { "exec" }, new HashSet<string> { "exec" }, ["real"], true, false);
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

    /// <summary>
    /// A blokky whose tables differ, validated {exec} and runnable {}: <c>validate</c> rejects only what isn't validated and
    /// <c>run</c> anything. Validation cases must go through <c>validate</c>, or they meet the runnable gate while being
    /// judged against the validated table (ADR 0009).
    /// </summary>
    [Test]
    [Arguments("[exec, log]", "log: hi", "Skip")]
    [Arguments("[exec]", "exec: [blokky-testkit, exit, '0']", "Pass")]
    public async Task Validation_cases_run_through_validate_and_are_judged_on_the_validated_table(string features, string node,
        string expected)
    {
        if (OperatingSystem.IsWindows() || !File.Exists("/usr/bin/true"))
        {
            Skip.Test("needs /usr/bin/true");
        }

        var root = Directory.CreateTempSubdirectory("conformance-tables-").FullName;
        try
        {
            var cases = Path.Combine(root, "cases", "area");
            Directory.CreateDirectory(cases);
            var file = Path.Combine(cases, "one.yaml");
            await File.WriteAllTextAsync(file, $$"""
                ---
                title: t
                rules: [SY-1]
                features: {{features}}
                plan: |
                    blokky: 1.0
                    {{node}}
                expect:
                    plan_errors: [{code: BK_PLAN_VALUE, line: 2, column: 1}]
                    exhaustive: true
                """);
            var binary = new TwoTableBinary();
            var caps = new AdapterCapabilities(new HashSet<string> { "exec" }, new HashSet<string>(), ["real"], true, false);
            var host = new HostInfo("linux", new Dictionary<string, string>(), new Dictionary<string, string>(), "/usr/bin/true", false);
            var settings = new RunnerSettings(Path.Combine(root, "run"), 1, 10, false);
            var runner = new CaseRunner(new BlokkyAdapter("/fake/blokky", binary, caps), host, settings);

            var result = await runner.RunAsync(Path.Combine(root, "cases"), file, CancellationToken.None);

            await Assert.That(binary.Commands).IsEquivalentTo(["validate"]);
            await Assert.That(result.Outcome.ToString()).IsEqualTo(expected);
            await Assert.That(result.Reason ?? "").DoesNotContain("not-supported check");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>The fake binary: <c>validate</c> lacks only <c>log</c>; <c>run</c> lacks <c>exec</c> too.</summary>
    private sealed class TwoTableBinary : IProcessRunner
    {
        public List<string> Commands { get; } = [];

        public Task<ProcessOutcome> RunAsync(ProcessSpec spec, CancellationToken cancellationToken)
        {
            Commands.Add(spec.Args[0]);
            var plan = System.Text.Encoding.UTF8.GetString(spec.Stdin);
            var missing = spec.Args[0] == "validate" ? (plan.Contains("log:", StringComparison.Ordinal) ? "log" : null) : "exec";
            var json = missing is null
                ? """{"status":"invalid","phase":"plan","errors":[{"code":"BK_PLAN_VALUE","message":"m","pos":"2:1"}]}"""
                : $$$"""
                    {"status":"invalid","phase":"plan","errors":[
                        {"code":"BK_PLAN_NOT_SUPPORTED","message":"m","detail":{"feature":"{{{missing}}}"}}]}
                    """;
            return Task.FromResult(AdapterTests.Exited(3, json));
        }
    }
}
