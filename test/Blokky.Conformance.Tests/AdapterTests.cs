using System.Text;
using Blokky.Conformance.Execution;
using Blokky.Conformance.Matching;

namespace Blokky.Conformance.Tests;

/// <summary>A process runner that records the spec and answers with a canned outcome.</summary>
internal sealed class FakeRunner(ProcessOutcome outcome) : IProcessRunner
{
    public ProcessSpec? Last { get; private set; }

    public Task<ProcessOutcome> RunAsync(ProcessSpec spec, CancellationToken cancellationToken)
    {
        Last = spec;
        return Task.FromResult(outcome);
    }
}

public class AdapterTests
{
    internal const string NotSupportedEnvelope =
        """
        {"schema_version":"0.1","status":"invalid","phase":"plan","errors":[
            {"code":"BK_PLAN_NOT_SUPPORTED","message":"no","node":"/","pos":"2:1","detail":{"feature":"log"}}]}
        """;

    internal static ProcessOutcome Exited(int code, string stdout, bool killed = false, bool deadline = false) =>
        new(code, Encoding.UTF8.GetBytes(stdout + "\n"), [], killed, deadline, TimeSpan.Zero);

    [Test]
    public async Task Inputs_go_in_order_as_input_flags_and_the_plan_on_stdin()
    {
        var fake = new FakeRunner(Exited(3, NotSupportedEnvelope));
        var adapter = new BlokkyAdapter("/bin/blokky", fake);
        var plan = "blokky: 1.0\nlog: hi\n"u8.ToArray();

        var env = new Dictionary<string, string>();
        var request = new RunRequest(plan, ["A=1", "NOEQUALS", "B=x=y"], env, "/w", TimeSpan.FromSeconds(60), []);

        await adapter.RunAsync(request, CancellationToken.None);

        await Assert.That(fake.Last!.Args).IsEquivalentTo(["run", "--input", "A=1", "--input", "NOEQUALS", "--input", "B=x=y", "-"]);
        await Assert.That(fake.Last.Stdin).IsEquivalentTo(plan);
        await Assert.That(fake.Last.Cwd).IsEqualTo("/w");
    }

    [Test]
    public async Task The_envelope_maps_to_plan_errors_with_line_and_column()
    {
        var outcome = BlokkyAdapter.Interpret(Exited(3, NotSupportedEnvelope));
        var error = (OrderedDictionary<string, object?>)outcome.PlanErrors[0]!;

        await Assert.That(outcome.Kind).IsEqualTo(OutcomeKind.Rejected);
        await Assert.That(outcome.Phase).IsEqualTo("plan");
        await Assert.That(error["line"]).IsEqualTo(2L);
        await Assert.That(error["column"]).IsEqualTo(1L);
        await Assert.That(error.ContainsKey("message")).IsFalse();
        await Assert.That(error.ContainsKey("pos")).IsFalse();
    }

    [Test]
    public async Task Usage_phase_input_errors_and_other_errors_are_told_apart()
    {
        var outcome = BlokkyAdapter.Interpret(Exited(2,
            """
            {"status":"invalid","phase":"usage","errors":[{"code":"BK_INPUT_SYNTAX","message":"m"},{"code":"BK_USAGE","message":"m"}]}
            """));

        await Assert.That(outcome.InputErrors.Count).IsEqualTo(1);
        await Assert.That(outcome.OtherErrors.Count).IsEqualTo(1);
    }

    [Test]
    public async Task Output_that_is_not_one_JSON_document_is_broken()
    {
        await Assert.That(BlokkyAdapter.Interpret(Exited(0, "{} {}")).Kind).IsEqualTo(OutcomeKind.Broken);
        await Assert.That(BlokkyAdapter.Interpret(Exited(0, "hello")).Kind).IsEqualTo(OutcomeKind.Broken);
        await Assert.That(BlokkyAdapter.Interpret(Exited(137, "", killed: true)).Kind).IsEqualTo(OutcomeKind.NoResult);
        await Assert.That(BlokkyAdapter.Interpret(Exited(137, "", deadline: true)).Kind).IsEqualTo(OutcomeKind.Deadline);
    }

    [Test]
    public async Task A_report_maps_to_the_result_document()
    {
        const string Report =
            """
            {"schema_version":"0.1","run_id":"01J","status":"interrupted","error":{"code":"BK_CANCELLED","message":"m","node":"/"},
                "inputs":{"B":{"value":"x","source":"cli"}},"duration_ms":1500,"outputs":{"T":{"secret":true},"U":null,"V":"v"},
                "nodes":[{"id":"/","kind":"do","pos":"2:1","status":"cancelled","duration_ms":1400,"children":[
                {"id":"/do/0","kind":"exec","pos":"3:5","status":"succeeded","argv":["blokky-testkit","exit","0"],"exit_code":0,
                    "attempts":{"count":2,"ended_by":"condition","items":[{"index":0,"status":"failed","duration_ms":3}]}}]}]}
            """;
        var outcome = BlokkyAdapter.Interpret(Exited(130, Report));
        var doc = outcome.Result!;
        var nodes = (OrderedDictionary<string, object?>)doc["nodes"]!;
        var matcher = new Matcher();

        await Assert.That(outcome.Kind).IsEqualTo(OutcomeKind.Result);
        await Assert.That(doc["status"]).IsEqualTo("stopped");
        await Assert.That(doc["outputs"] is OrderedDictionary<string, object?> o && o["T"] == SecretValue.Value).IsTrue();
        await Assert.That(matcher.Match(Values.Yaml("{ B: { value: x, source: supplied } }"), doc["inputs"], "inputs")).IsNull();
        await Assert.That(matcher.Match(Values.Yaml("{ $min: 1s, $max: 2s }"), doc["duration"], "duration")).IsNull();
        await Assert.That(matcher.Match(Values.Yaml(
            "{ /: { kind: do, children: [/do/0] }, /do/0: { invocation: [blokky-testkit, exit, '0'], exit_code: 0, "
            + "repetition: { count: 2, entries: [{ index: 0, status: failed }] } } }"),
            nodes, "nodes")).IsNull();
    }
}
