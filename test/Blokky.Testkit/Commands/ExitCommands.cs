using System.Runtime.Versioning;
using Blokky.Testkit.Interop;

namespace Blokky.Testkit.Commands;

/// <summary><c>exit</c> (TK-9) and <c>die</c> (TK-21).</summary>
internal static class ExitCommands
{
    public static int Exit(IReadOnlyList<Arg> args)
    {
        var parsed = Testkit.Prepare(new CommandSpec("exit"), args);
        return Grammar.ExitCode("exit", parsed.Single("CODE").Text);
    }

    public static int Die(IReadOnlyList<Arg> args)
    {
        var parsed = Testkit.Prepare(new CommandSpec("die").Value("--signal"), args);
        parsed.NoPositionals();
        var name = parsed.GetText("--signal") ?? throw TestkitException.Usage("die: --signal is required");
        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
        {
            return DieUnix(name);
        }

        throw TestkitException.NotSupported("die");
    }

    [SupportedOSPlatform("linux")]
    [SupportedOSPlatform("macos")]
    private static unsafe int DieUnix(string name)
    {
        var signal = Libc.SignalNumber(name) ?? throw TestkitException.Usage($"die: unknown or non-terminating signal '{name}'");

        // No core file in the case's work dir: the parent still sees the signal.
        var noCore = new Libc.RLimit { Current = 0, Maximum = 0 };
        _ = Libc.SetRlimit(Libc.RlimitCore, &noCore);

        // Default disposition (the runtime may have its own handler), then the signal to itself (docs/design/testkit.md §2).
        _ = Libc.Signal(signal, 0);
        _ = Libc.Kill(Libc.GetPid(), signal);

        // The signal ends the process; reaching here means it was blocked or delivered late.
        Thread.Sleep(TimeSpan.FromSeconds(5));
        throw TestkitException.Internal($"die: still alive after {name}");
    }
}
