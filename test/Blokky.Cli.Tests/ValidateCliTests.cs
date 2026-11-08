using System.Collections.Frozen;
using System.Text;
using System.Text.Json;

namespace Blokky.Cli.Tests;

/// <summary><c>validate</c>'s output (cli.md "validate output"): the verdict when the plan passes, the envelope when it doesn't.</summary>
public class ValidateCliTests
{
    private const string Plan = "blokky: 1.0\nname: greet\nlog: hi\n";

    private static readonly FrozenSet<string> LogOnly = FrozenSet.Create(StringComparer.Ordinal, "log");

    private static readonly FrozenSet<string> Several = FrozenSet.Create(StringComparer.Ordinal, "do", "exec", "log", "timeout");

    private static CliResult Run(string plan, IReadOnlySet<string> validated, params string[] args)
    {
        using var input = new MemoryStream(Encoding.UTF8.GetBytes(plan));
        using var stdout = new MemoryStream();
        using var stderr = new MemoryStream();
        var code = CliApp.Run(args, input, stdout, stderr, validated, FrozenSet<string>.Empty);
        return new CliResult(code, Encoding.UTF8.GetString(stdout.ToArray()), Encoding.UTF8.GetString(stderr.ToArray()));
    }

    [Test]
    public async Task A_valid_plan_with_validated_features_prints_the_allow_verdict_and_exits_0()
    {
        var result = Run(Plan, LogOnly, "validate", "-");
        using var json = result.Json();
        var root = json.RootElement;

        await Assert.That(result.ExitCode).IsEqualTo(0);
        await Assert.That(result.Stderr).IsEqualTo("");
        await Assert.That(result.Stdout.EndsWith("}\n", StringComparison.Ordinal)).IsTrue();
        await Assert.That(string.Join(",", root.EnumerateObject().Select(p => p.Name))).IsEqualTo("schema_version,status,decision,plan");
        await Assert.That(root.GetProperty("status").GetString()).IsEqualTo("valid");
        await Assert.That(root.GetProperty("decision").GetString()).IsEqualTo("allow");
        await Assert.That(root.GetProperty("plan").GetProperty("spec_version").GetString()).IsEqualTo("1.0");
        await Assert.That(root.GetProperty("plan").GetProperty("source").GetString()).IsEqualTo("stdin");
        await Assert.That(root.GetProperty("plan").GetProperty("sha256").GetString())
            .IsEqualTo(Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(Plan))));
    }

    [Test]
    public async Task Run_gates_on_runnable_even_when_the_plan_validates()
    {
        var result = Run(Plan, LogOnly, "run", "-");
        using var json = result.Json();
        var error = json.RootElement.GetProperty("errors")[0];

        await Assert.That(result.ExitCode).IsEqualTo(3);
        await Assert.That(error.GetProperty("code").GetString()).IsEqualTo("BK_PLAN_NOT_SUPPORTED");
        await Assert.That(error.GetProperty("message").GetString()).Contains("can't run");
    }

    [Test]
    public async Task Every_plan_error_is_reported_with_its_node_position_and_rule()
    {
        var plan = "blokky: 1.0\ndo:\n  - timeout: 30\n    exec: [a]\n  - log: x\n    level: loud\n    exe: a\n";
        var result = Run(plan, Several, "validate", "-");
        using var json = result.Json();
        var errors = json.RootElement.GetProperty("errors").EnumerateArray().Select(e =>
            $"{e.GetProperty("code").GetString()} {e.GetProperty("node").GetString()} {e.GetProperty("pos").GetString()} "
            + (e.TryGetProperty("detail", out var d)
                ? d.GetProperty(d.TryGetProperty("rule", out _) ? "rule" : "suggestion").GetString()
                : ""));

        await Assert.That(result.ExitCode).IsEqualTo(3);
        await Assert.That(json.RootElement.GetProperty("phase").GetString()).IsEqualTo("plan");
        await Assert.That(string.Join("; ", errors)).IsEqualTo(
            "BK_PLAN_VALUE /do/0 3:14 SY-57; BK_PLAN_VALUE /do/1 6:12 SY-88; BK_PLAN_UNKNOWN_KEY /do/1 7:5 exec");
        await Assert.That(result.Stderr.Split('\n')[0])
            .StartsWith("blokky: error BK_PLAN_VALUE at 3:14 in /do/0: timeout: '30' is a bare number");
        await Assert.That(json.RootElement.GetProperty("errors")[0].GetProperty("fix").GetString()).IsEqualTo("timeout: 30s");
    }

    [Test]
    public async Task A_plan_file_reports_its_absolute_path_as_the_source()
    {
        var path = Path.Combine(Path.GetTempPath(), $"blokky-validate-{Guid.NewGuid():N}.blokky.yaml");
        await File.WriteAllTextAsync(path, Plan);
        try
        {
            var result = Run("", LogOnly, "validate", path);
            using var json = result.Json();

            await Assert.That(result.ExitCode).IsEqualTo(0);
            await Assert.That(json.RootElement.GetProperty("plan").GetProperty("source").GetString()).IsEqualTo(path);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
