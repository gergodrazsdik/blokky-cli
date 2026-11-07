using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace Blokky.Testkit.Commands;

/// <summary><c>sleep</c> (TK-14): sleep, optionally ignoring and recording graceful stop requests.</summary>
internal static class SleepCommand
{
    /// <summary>The handlers, once registered: by <see cref="RegisterEarly"/> or by <see cref="Run"/>.</summary>
    private static List<PosixSignalRegistration>? _registrations;

    private static CommandSpec Spec() => new CommandSpec("sleep")
        .Flag("--ignore-term")
        .Value("--ignore-term-for")
        .Value("--record-signals")
        .Value("--tag")
        .Value("--exit");

    /// <summary>
    /// Registers the handlers before the invocation log is written (which can wait for the log's lock), so a request that
    /// arrives once the process has started is already handled. Invalid arguments are left to <see cref="Run"/>, which
    /// reports them after the log line (TK-5).
    /// </summary>
    public static void RegisterEarly(IReadOnlyList<Arg> args)
    {
        try
        {
            Register(Options(Spec().Parse(args)));
        }
        catch (TestkitException)
        {
            // Reported by Run.
        }
    }

    public static int Run(IReadOnlyList<Arg> args)
    {
        var parsed = Spec().Parse(args);
        var options = Options(parsed);
        Register(options);

        // The common options (TK-7) after the handlers: a case that waits for --pid-file and then signals hits a handler.
        Testkit.ApplyCommon(parsed);
        Thread.Sleep(options.Duration);
        GC.KeepAlive(_registrations);
        return options.ExitCode;
    }

    private static SleepOptions Options(ParsedArgs parsed)
    {
        var duration = Grammar.Duration("sleep", parsed.Single("DURATION").Text);
        var exitCode = parsed.GetText("--exit") is { } code ? Grammar.ExitCode("--exit", code) : 0;
        if (parsed.Has("--ignore-term") && parsed.Has("--ignore-term-for"))
        {
            throw TestkitException.Usage("sleep: --ignore-term and --ignore-term-for exclude each other");
        }

        var ignoreFor = parsed.Has("--ignore-term")
            ? Timeout.InfiniteTimeSpan
            : parsed.GetText("--ignore-term-for") is { } window ? Grammar.Duration("--ignore-term-for", window) : TimeSpan.Zero;
        return new SleepOptions(duration, exitCode, ignoreFor, parsed.GetText("--record-signals"), parsed.GetText("--tag"));
    }

    private static void Register(SleepOptions options)
    {
        if (_registrations is not null)
        {
            return;
        }

        _registrations = [];
        if (options.IgnoreFor == TimeSpan.Zero && options.RecordTo is null)
        {
            return;
        }

        foreach (var (signal, name) in GracefulStopRequests())
        {
            _registrations.Add(PosixSignalRegistration.Create(signal, context =>
            {
                // Recorded before it's acted on or ignored (TK-14).
                if (options.RecordTo is not null)
                {
                    var line = options.Tag is null ? $"{name}\n" : $"{options.Tag} {name}\n";
                    try
                    {
                        Files.AppendLocked(options.RecordTo, Encoding.UTF8.GetBytes(line), "--record-signals");
                    }
                    catch (TestkitException e)
                    {
                        StdStream.Diagnostic(e.Message);
                    }
                }

                // --ignore-term-for counts from the process's start (Testkit.Started), not from this registration.
                if (options.IgnoreFor == Timeout.InfiniteTimeSpan || Stopwatch.GetElapsedTime(Testkit.Started) < options.IgnoreFor)
                {
                    context.Cancel = true;
                }
            }));
        }
    }

    /// <summary>The graceful stop requests of EM-26 and the names recorded for them.</summary>
    private static (PosixSignal Signal, string Name)[] GracefulStopRequests() => OperatingSystem.IsWindows()
        ? [(PosixSignal.SIGQUIT, "CTRL_BREAK"), (PosixSignal.SIGINT, "CTRL_C")]
        : [(PosixSignal.SIGTERM, "SIGTERM"), (PosixSignal.SIGINT, "SIGINT"), (PosixSignal.SIGHUP, "SIGHUP")];

    private sealed record SleepOptions(TimeSpan Duration, int ExitCode, TimeSpan IgnoreFor, string? RecordTo, string? Tag);
}
