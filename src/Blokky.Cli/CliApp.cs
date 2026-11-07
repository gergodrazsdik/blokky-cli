using System.Reflection;
using System.Text;
using Blokky.Core.Contracts;
using Blokky.Core.Errors;
using Blokky.Core.Features;
using Blokky.Core.Plans;
using Blokky.Core.Reading;

namespace Blokky.Cli;

/// <summary>
/// The CLI host: parses argv, reads the plan and reports. Streams are passed in so tests drive it in-process.
/// This build executes nothing: a plan is read (<see cref="PlanReader"/>), and one that reads cleanly stops at the feature
/// gate (<see cref="StubFeatureGate"/>).
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

        // validate and run share the static front half: the plan reader, then (until the AST lands) the stub gate.
        var read = new PlanReader().Read(plan.Bytes.Span);
        if (!read.Ok)
        {
            return Fail(new PreRunEnvelope(Phase.Plan, read.Errors), stdout, stderr);
        }

        var table = command.Kind == CommandKind.Validate ? FeatureTable.Validated : FeatureTable.Runnable;
        var planErrors = StubFeatureGate.Check(plan, table);
        return Fail(new PreRunEnvelope(Phase.Plan, planErrors), stdout, stderr);
    }

    internal static string Version { get; } =
        typeof(CliApp).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown";

    /// <summary>
    /// Reads the plan, but never more than one byte over the size cap (SY-9): that byte is enough for the reader to reject
    /// the plan, and a huge input (or <c>/dev/zero</c>) isn't buffered in full.
    /// </summary>
    private static (PlanSource?, BlokkyError?) ReadPlan(string path, Stream stdin)
    {
        if (path == "-")
        {
            return (new PlanSource(ReadCapped(stdin), PlanSource.Stdin), null);
        }

        string fullPath = path;
        try
        {
            fullPath = Path.GetFullPath(path);
            using var file = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            return (new PlanSource(ReadCapped(file), fullPath), null);
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

    /// <summary>At most <see cref="PlanText.MaxBytes"/> + 1 bytes of <paramref name="stream"/>.</summary>
    internal static byte[] ReadCapped(Stream stream)
    {
        const int Limit = PlanText.MaxBytes + 1;
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while (buffer.Length < Limit && (read = stream.Read(chunk, 0, (int)Math.Min(chunk.Length, Limit - buffer.Length))) > 0)
        {
            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
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
            if (lines.Length >= 64 * 1024)
            {
                // Written in chunks: a plan can have an error per character (SY-13).
                WriteText(stderr, lines.ToString());
                lines.Clear();
            }
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
