using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Blokky.Core.Contracts;
using Blokky.Core.Errors;

namespace Blokky.Core.Tests;

public class ContractTests
{
    private static readonly BlokkyError NotSupported = new()
    {
        Code = ErrorCodes.PlanNotSupported,
        Message = "not supported",
        Node = "/",
        Pos = "2:1",
        Detail = new JsonObject { ["feature"] = "log" },
    };

    [Test]
    public async Task Envelope_is_one_compact_json_document_with_trailing_lf_and_no_bom()
    {
        var bytes = ContractWriter.ToUtf8(new PreRunEnvelope(Phase.Plan, [NotSupported]));

        await Assert.That(bytes[0]).IsEqualTo((byte)'{');
        await Assert.That(bytes[^1]).IsEqualTo((byte)'\n');
        await Assert.That(Encoding.UTF8.GetString(bytes)).IsEqualTo(
            """{"schema_version":"0.1","status":"invalid","phase":"plan","errors":[""" +
            """{"code":"BK_PLAN_NOT_SUPPORTED","message":"not supported","node":"/","pos":"2:1","detail":{"feature":"log"}}]}""" +
            "\n");
    }

    [Test]
    public async Task Envelope_omits_null_error_fields()
    {
        var bytes = ContractWriter.ToUtf8(new PreRunEnvelope(Phase.Usage, [new BlokkyError { Code = ErrorCodes.Usage, Message = "m" }]));
        using var doc = JsonDocument.Parse(bytes);
        var error = doc.RootElement.GetProperty("errors")[0];

        await Assert.That(doc.RootElement.GetProperty("phase").GetString()).IsEqualTo("usage");
        await Assert.That(error.EnumerateObject().Select(p => p.Name).ToArray()).IsEquivalentTo(["code", "message"]);
    }

    [Test]
    public async Task Envelope_escapes_only_what_json_requires()
    {
        var error = new BlokkyError { Code = ErrorCodes.Usage, Message = "can't read 'plän' \"x\"\u001b" };
        var text = Encoding.UTF8.GetString(ContractWriter.ToUtf8(new PreRunEnvelope(Phase.Usage, [error])));

        await Assert.That(text).Contains("""can't read 'plän' \"x\"\u001B""");
    }

    [Test]
    public async Task Envelope_requires_an_error()
    {
        await Assert.That(() => new PreRunEnvelope(Phase.Plan, [])).Throws<ArgumentException>();
    }

    [Test]
    [Arguments(Phase.Usage, ErrorCodes.Usage, 2)]
    [Arguments(Phase.Plan, ErrorCodes.PlanNotSupported, 3)]
    [Arguments(Phase.Config, "BK_CONFIG", 4)]
    [Arguments(Phase.Approval, "BK_APPROVAL_REQUIRED", 5)]
    [Arguments(Phase.Approval, "BK_DENIED", 6)]
    [Arguments(Phase.Approval, "BK_SHA_MISMATCH", 7)]
    public async Task Phase_maps_to_the_exit_code_in_cli_md(Phase phase, string code, int exitCode)
    {
        var envelope = new PreRunEnvelope(phase, [new BlokkyError { Code = code, Message = "m" }]);

        await Assert.That(envelope.ExitCode).IsEqualTo(exitCode);
    }

    [Test]
    [Arguments("plain", "plain")]
    [Arguments("a\u001b[31mb", "a\\x1b[31mb")]
    [Arguments("tab\there", "tab\\x09here")]
    [Arguments("c1\u009bx", "c1\\x9bx")]
    public async Task Display_text_escapes_control_characters(string input, string expected)
    {
        await Assert.That(DisplayText.Escape(input)).IsEqualTo(expected);
    }
}
