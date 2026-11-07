using System.Text.Json;

namespace Blokky.Cli.Tests;

public class StubCliTests
{
    private const string Plan = "blokky: 1.0\nlog: hi\n";

    [Test]
    [Arguments("run")]
    [Arguments("validate")]
    public async Task Plan_from_stdin_is_not_supported_with_exit_3(string command)
    {
        var result = CliHarness.Run(Plan, command, "--input", "BRANCH=main", "-");
        using var json = result.Json();
        var root = json.RootElement;
        var error = root.GetProperty("errors")[0];

        await Assert.That(result.ExitCode).IsEqualTo(3);
        await Assert.That(root.GetProperty("schema_version").GetString()).IsEqualTo("0.1");
        await Assert.That(root.GetProperty("status").GetString()).IsEqualTo("invalid");
        await Assert.That(root.GetProperty("phase").GetString()).IsEqualTo("plan");
        await Assert.That(root.GetProperty("errors").GetArrayLength()).IsEqualTo(1);
        await Assert.That(error.GetProperty("code").GetString()).IsEqualTo("BK_PLAN_NOT_SUPPORTED");
        await Assert.That(error.GetProperty("detail").GetProperty("feature").GetString()).IsEqualTo("log");
        await Assert.That(error.GetProperty("pos").GetString()).IsEqualTo("2:1");
        await Assert.That(root.TryGetProperty("run_id", out _)).IsFalse();
    }

    [Test]
    public async Task Stdout_carries_exactly_one_document_and_one_lf()
    {
        var result = CliHarness.Run(Plan, "run", "-");

        await Assert.That(result.Stdout.EndsWith("}\n", StringComparison.Ordinal)).IsTrue();
        await Assert.That(result.Stdout.Count(c => c == '\n')).IsEqualTo(1);
        await Assert.That(result.Stderr).StartsWith("blokky: error BK_PLAN_NOT_SUPPORTED at 2:1: ");
    }

    [Test]
    public async Task Plan_file_is_read_from_disk()
    {
        var path = Path.Combine(Path.GetTempPath(), $"blokky-cli-test-{Guid.NewGuid():N}.blokky.yaml");
        await File.WriteAllTextAsync(path, "blokky: 1.0\nexec: [git, status]\n");
        try
        {
            var result = CliHarness.Run("", "validate", path);
            using var json = result.Json();

            await Assert.That(result.ExitCode).IsEqualTo(3);
            var detail = json.RootElement.GetProperty("errors")[0].GetProperty("detail");
            await Assert.That(detail.GetProperty("feature").GetString()).IsEqualTo("exec");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public async Task Missing_plan_file_is_a_usage_error()
    {
        var result = CliHarness.Run("", "run", "/nonexistent/blokky/plan.yaml");
        using var json = result.Json();
        var error = json.RootElement.GetProperty("errors")[0];

        await Assert.That(result.ExitCode).IsEqualTo(2);
        await Assert.That(json.RootElement.GetProperty("phase").GetString()).IsEqualTo("usage");
        await Assert.That(error.GetProperty("code").GetString()).IsEqualTo("BK_USAGE");
        await Assert.That(error.GetProperty("message").GetString()).Contains("no such file");
    }

    [Test]
    public async Task Argument_errors_are_all_reported_before_the_plan_is_read()
    {
        var result = CliHarness.Run(
            Plan, "run", "--input=X=1", "--input", "my-input=1", "--input", "NOEQUALS", "--verbose", "a.yaml", "b.yaml");
        using var json = result.Json();
        var codes = json.RootElement.GetProperty("errors").EnumerateArray().Select(e => e.GetProperty("code").GetString()!).ToArray();

        await Assert.That(result.ExitCode).IsEqualTo(2);
        await Assert.That(string.Join(",", codes)).IsEqualTo("BK_USAGE,BK_INPUT_SYNTAX,BK_INPUT_SYNTAX,BK_USAGE,BK_USAGE");
    }

    [Test]
    [Arguments("")]
    [Arguments("lint -")]
    [Arguments("run")]
    [Arguments("run --input")]
    [Arguments("--version run")]
    public async Task Malformed_invocations_exit_2_with_an_envelope(string argLine)
    {
        var result = CliHarness.Run(Plan, argLine.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        using var json = result.Json();

        await Assert.That(result.ExitCode).IsEqualTo(2);
        await Assert.That(json.RootElement.GetProperty("errors")[0].GetProperty("code").GetString()).IsEqualTo("BK_USAGE");
    }

    [Test]
    public async Task Empty_input_value_and_double_dash_are_accepted()
    {
        var result = CliHarness.Run(Plan, "run", "--input", "NOTE=", "--", "-");

        await Assert.That(result.ExitCode).IsEqualTo(3);
    }

    [Test]
    public async Task Version_prints_the_version()
    {
        var result = CliHarness.Run("", "--version");

        await Assert.That(result.ExitCode).IsEqualTo(0);
        await Assert.That(result.Stdout).StartsWith("blokky 0.1.0-dev");
        await Assert.That(result.Stdout.EndsWith('\n')).IsTrue();
    }

    [Test]
    public async Task Help_prints_usage()
    {
        var result = CliHarness.Run("", "--help");

        await Assert.That(result.ExitCode).IsEqualTo(0);
        await Assert.That(result.Stdout).Contains("blokky validate [--input NAME=VALUE]");
    }

    [Test]
    public async Task Control_characters_in_paths_are_escaped_on_stderr_and_json_escaped_on_stdout()
    {
        var result = CliHarness.Run("", "run", "/nonexistent/\u001b[2Jx.yaml");

        await Assert.That(result.Stderr).Contains("\\x1b[2J");
        await Assert.That(result.Stderr).DoesNotContain("\u001b");
        await Assert.That(result.Stdout).DoesNotContain("\u001b");
        await Assert.That(() => JsonDocument.Parse(result.Stdout)).ThrowsNothing();
    }
}
