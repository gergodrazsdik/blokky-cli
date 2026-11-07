using System.Text;
using System.Text.Json;

namespace Blokky.Cli.Tests;

/// <summary>The outcome of one <c>blokky</c> invocation.</summary>
internal sealed record CliResult(int ExitCode, string Stdout, string Stderr)
{
    public JsonDocument Json() => JsonDocument.Parse(Stdout);
}

/// <summary>Runs the CLI in-process with captured streams.</summary>
internal static class CliHarness
{
    public static CliResult Run(string stdin, params string[] args)
    {
        using var input = new MemoryStream(Encoding.UTF8.GetBytes(stdin));
        using var stdout = new MemoryStream();
        using var stderr = new MemoryStream();
        var code = CliApp.Run(args, input, stdout, stderr);
        return new CliResult(code, Encoding.UTF8.GetString(stdout.ToArray()), Encoding.UTF8.GetString(stderr.ToArray()));
    }
}
