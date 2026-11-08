using System.Diagnostics;
using System.Text.Json;

namespace Blokky.Cli.Tests;

/// <summary>Skips unless <c>BLOKKY_BIN</c> names the published AOT binary (<c>just test-aot</c>, CI).</summary>
internal sealed class RequiresPublishedBinaryAttribute() : SkipAttribute("BLOKKY_BIN is not set (run just test-aot)")
{
    public override Task<bool> ShouldSkip(TestRegisteredContext context) =>
        Task.FromResult(string.IsNullOrEmpty(Environment.GetEnvironmentVariable("BLOKKY_BIN")));
}

/// <summary>CLI tests against the published Native AOT binary (validation-strategy.md: CLI and AOT smoke tiers).</summary>
public class PublishedBinaryTests
{
    [Test]
    [RequiresPublishedBinary]
    public async Task Aot_binary_rejects_a_stdin_plan_as_not_supported()
    {
        var (code, stdout, stderr) = await RunAsync("blokky: 1.0\nlog: hi\n", "run", "-");
        using var json = JsonDocument.Parse(stdout);
        var error = json.RootElement.GetProperty("errors")[0];

        await Assert.That(code).IsEqualTo(3);
        await Assert.That(error.GetProperty("code").GetString()).IsEqualTo("BK_PLAN_NOT_SUPPORTED");
        await Assert.That(error.GetProperty("detail").GetProperty("feature").GetString()).IsEqualTo("log");
        await Assert.That(stderr).StartsWith("blokky: error BK_PLAN_NOT_SUPPORTED");
    }

    [Test]
    [RequiresPublishedBinary]
    public async Task Aot_binary_reports_reader_errors_with_positions_and_rules()
    {
        var (code, stdout, _) = await RunAsync("blokky: 1.0\nname: &t x\nlog: *t\nlog: y\n", "run", "-");
        using var json = JsonDocument.Parse(stdout);
        var errors = json.RootElement.GetProperty("errors").EnumerateArray()
            .Select(e => $"{e.GetProperty("code").GetString()} {e.GetProperty("pos").GetString()} "
                + (e.GetProperty("detail").TryGetProperty("rule", out var rule) ? rule : e.GetProperty("detail").GetProperty("feature"))
                    .GetString());

        await Assert.That(code).IsEqualTo(3);
        await Assert.That(string.Join("; ", errors))
            .IsEqualTo("BK_PLAN_YAML 2:7 SY-14; BK_PLAN_YAML 3:6 SY-14; BK_PLAN_DUPLICATE_KEY 4:1 SY-18; BK_PLAN_NOT_SUPPORTED 3:1 log");
    }

    [Test]
    [RequiresPublishedBinary]
    public async Task Aot_binary_reports_schema_errors_with_their_node()
    {
        var (code, stdout, stderr) = await RunAsync("blokky: 1.0\ndo:\n  - timeout: 30\n    exec: [a]\n    retries: 3\n", "validate", "-");
        using var json = JsonDocument.Parse(stdout);
        var errors = json.RootElement.GetProperty("errors").EnumerateArray()
            .Select(e => $"{e.GetProperty("code").GetString()} {e.GetProperty("pos").GetString()} {e.GetProperty("node").GetString()}")
            .ToList();

        await Assert.That(code).IsEqualTo(3);
        await Assert.That(errors[0]).IsEqualTo("BK_PLAN_VALUE 3:14 /do/0");
        await Assert.That(errors[1]).IsEqualTo("BK_PLAN_UNKNOWN_KEY 5:5 /do/0");
        await Assert.That(stderr).StartsWith("blokky: error BK_PLAN_VALUE at 3:14 in /do/0: ");
    }

    [Test]
    [RequiresPublishedBinary]
    public async Task Aot_binary_prints_its_version()
    {
        var (code, stdout, _) = await RunAsync("", "--version");

        await Assert.That(code).IsEqualTo(0);
        await Assert.That(stdout).StartsWith("blokky 0.1.0-dev");
    }

    private static async Task<(int Code, string Stdout, string Stderr)> RunAsync(string stdin, params string[] args)
    {
        var psi = new ProcessStartInfo(Environment.GetEnvironmentVariable("BLOKKY_BIN")!)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var arg in args)
        {
            psi.ArgumentList.Add(arg);
        }

        using var process = Process.Start(psi)!;
        await process.StandardInput.WriteAsync(stdin);
        process.StandardInput.Close();
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30));
        return (process.ExitCode, await stdout, await stderr);
    }
}
