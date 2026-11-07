using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace Blokky.Testkit.Commands;

/// <summary><c>sleep</c> (TK-14): sleep, optionally ignoring and recording graceful stop requests.</summary>
internal static class SleepCommand
{
    public static int Run(IReadOnlyList<Arg> args)
    {
        var started = Stopwatch.StartNew();
        var parsed = Testkit.Prepare(
            new CommandSpec("sleep")
                .Flag("--ignore-term")
                .Value("--ignore-term-for")
                .Value("--record-signals")
                .Value("--tag")
                .Value("--exit"),
            args);
        var duration = Grammar.Duration("sleep", parsed.Single("DURATION").Text);
        var exitCode = parsed.GetText("--exit") is { } code ? Grammar.ExitCode("--exit", code) : 0;
        if (parsed.Has("--ignore-term") && parsed.Has("--ignore-term-for"))
        {
            throw TestkitException.Usage("sleep: --ignore-term and --ignore-term-for exclude each other");
        }

        TimeSpan ignoreFor = parsed.Has("--ignore-term")
            ? Timeout.InfiniteTimeSpan
            : parsed.GetText("--ignore-term-for") is { } window ? Grammar.Duration("--ignore-term-for", window) : TimeSpan.Zero;
        var recordTo = parsed.GetText("--record-signals");
        var tag = parsed.GetText("--tag");

        var registrations = new List<PosixSignalRegistration>();
        if (ignoreFor != TimeSpan.Zero || recordTo is not null)
        {
            foreach (var (signal, name) in GracefulStopRequests())
            {
                registrations.Add(PosixSignalRegistration.Create(signal, context =>
                {
                    // Recorded before it's acted on or ignored (TK-14).
                    if (recordTo is not null)
                    {
                        var line = tag is null ? $"{name}\n" : $"{tag} {name}\n";
                        try
                        {
                            Files.AppendLocked(recordTo, Encoding.UTF8.GetBytes(line), "--record-signals");
                        }
                        catch (TestkitException e)
                        {
                            StdStream.Diagnostic(e.Message);
                        }
                    }

                    if (ignoreFor == Timeout.InfiniteTimeSpan || started.Elapsed < ignoreFor)
                    {
                        context.Cancel = true;
                    }
                }));
            }
        }

        Thread.Sleep(duration);
        GC.KeepAlive(registrations);
        return exitCode;
    }

    /// <summary>The graceful stop requests of EM-26 and the names recorded for them.</summary>
    private static (PosixSignal Signal, string Name)[] GracefulStopRequests() => OperatingSystem.IsWindows()
        ? [(PosixSignal.SIGQUIT, "CTRL_BREAK"), (PosixSignal.SIGINT, "CTRL_C")]
        : [(PosixSignal.SIGTERM, "SIGTERM"), (PosixSignal.SIGINT, "SIGINT"), (PosixSignal.SIGHUP, "SIGHUP")];
}
