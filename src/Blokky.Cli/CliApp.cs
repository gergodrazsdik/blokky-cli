using System.Reflection;
using System.Text;
using Blokky.Core.Contracts;
using Blokky.Core.Errors;
using Blokky.Core.Plans;

namespace Blokky.Cli;

/// <summary>
/// The CLI host: parses argv, reads the plan and reports. Streams are passed in so tests drive it in-process.
/// This build executes nothing: every plan stops at the feature gate (<see cref="StubFeatureGate"/>).
/// </summary>
internal static class CliApp
{
    public static int Run(IReadOnlyList<string> args, Stream stdin, Stream stdout, Stream stderr)
    {
        ArgumentNullException.ThrowIfNull(stdin);
        ArgumentNullException.ThrowIfNull(stdout);
        ArgumentNullException.ThrowIfNull(stderr);
        try
        {
            return Dispatch(args, stdin, stdout, stderr);
        }
#pragma warning disable CA1031 // Last-resort handler: any unexpected exception is BK_INTERNAL (exit 70).
        catch (Exception ex)
#pragma warning restore CA1031
        {
            var message = DisplayText.Escape(ex.Message);
            WriteText(stderr, $"blokky: error {ErrorCodes.Internal}: {message} (this is a bug in blokky; please report it)\n");
            return ExitCodes.Internal;
        }
    }

    private static int Dispatch(IReadOnlyList<string> args, Stream stdin, Stream stdout, Stream stderr)
    {
        var (command, usageErrors) = CommandLine.Parse(args);
        if (command is null)
        {
            return Fail(new PreRunEnvelope(Phase.Usage, usageErrors), stdout, stderr);
        }

        switch (command.Kind)
        {
            case CommandKind.Help:
                WriteText(stdout, CommandLine.UsageText + "\n");
                return ExitCodes.Success;
            case CommandKind.Version:
                WriteText(stdout, $"blokky {Version}\n");
                return ExitCodes.Success;
        }

        var (plan, readError) = ReadPlan(command.PlanPath!, stdin);
        if (plan is null)
        {
            return Fail(new PreRunEnvelope(Phase.Usage, [readError!]), stdout, stderr);
        }

        // validate and run share the static front half; nothing past it exists yet.
        var planErrors = StubFeatureGate.Check(plan);
        return Fail(new PreRunEnvelope(Phase.Plan, planErrors), stdout, stderr);
    }

    internal static string Version { get; } =
        typeof(CliApp).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown";

    private static (PlanSource?, BlokkyError?) ReadPlan(string path, Stream stdin)
    {
        if (path == "-")
        {
            using var buffer = new MemoryStream();
            stdin.CopyTo(buffer);
            return (new PlanSource(buffer.ToArray(), PlanSource.Stdin), null);
        }

        string fullPath = path;
        try
        {
            fullPath = Path.GetFullPath(path);
            return (new PlanSource(File.ReadAllBytes(fullPath), fullPath), null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            var reason = ex switch
            {
                FileNotFoundException or DirectoryNotFoundException => "no such file",
                UnauthorizedAccessException => "permission denied or not a regular file",
                _ => ex.Message,
            };
            return (null, new BlokkyError
            {
                Code = ErrorCodes.Usage,
                Message = $"can't read plan file {fullPath}: {reason}",
            });
        }
    }

    /// <summary>Writes the envelope to stdout and one error line per error to stderr (OC-1, OC-2).</summary>
    private static int Fail(PreRunEnvelope envelope, Stream stdout, Stream stderr)
    {
        WriteBytes(stdout, ContractWriter.ToUtf8(envelope));
        var lines = new StringBuilder();
        foreach (var error in envelope.Errors)
        {
            lines.Append("blokky: error ").Append(error.Code);
            if (error.Pos is not null)
            {
                lines.Append(" at ").Append(error.Pos);
            }

            lines.Append(": ").Append(DisplayText.Escape(error.Message)).Append('\n');
        }

        WriteText(stderr, lines.ToString());
        return envelope.ExitCode;
    }

    private static void WriteText(Stream stream, string text) => WriteBytes(stream, Encoding.UTF8.GetBytes(text));

    /// <summary>Raw UTF-8, no BOM. A closed stream doesn't change the exit code (OC-6).</summary>
    private static void WriteBytes(Stream stream, byte[] bytes)
    {
        try
        {
            stream.Write(bytes);
            stream.Flush();
        }
        catch (IOException)
        {
            // Closed pipe (blokky … | head): ignored.
        }
    }
}
