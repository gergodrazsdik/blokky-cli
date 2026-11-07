using System.Runtime.InteropServices;
using Blokky.Testkit.Interop;

namespace Blokky.Testkit.Commands;

/// <summary><c>info</c> (TK-17): pid, user, cwd, platform and terminal as one JSON object.</summary>
internal static class InfoCommand
{
    public static int Run(IReadOnlyList<Arg> args)
    {
        var parsed = Testkit.Prepare(new CommandSpec("info"), args);
        parsed.NoPositionals();
        var unix = OperatingSystem.IsLinux() || OperatingSystem.IsMacOS();
        var cwd = Directory.GetCurrentDirectory();
        uint? uid = null, euid = null;
        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
        {
            uid = Libc.GetUid();
            euid = Libc.GetEuid();
        }

        StdStream.Out.Write(Json.Line(w =>
        {
            w.WriteStartObject();
            w.WriteNumber("pid", Environment.ProcessId);
            w.WriteNumber("ppid", Invocation.ParentProcessId());
            w.WriteString("user", Environment.UserName);
            if (unix)
            {
                w.WriteNumber("uid", uid!.Value);
                w.WriteNumber("euid", euid!.Value);
            }
            else
            {
                w.WriteNull("uid");
                w.WriteNull("euid");
            }

            // Windows: an elevated token; Unix: euid 0.
            w.WriteBoolean("elevated", Environment.IsPrivilegedProcess);
            w.WriteString("cwd", cwd);
            w.WriteString("os", OperatingSystem.IsWindows() ? "windows" : OperatingSystem.IsMacOS() ? "macos" : "linux");
            w.WriteString("arch", RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant());
            w.WriteBoolean("tty", HasTerminal());
            w.WriteString("stdin", StdinKind());
            w.WriteEndObject();
        }));
        return 0;
    }

    /// <summary>Unix: whether <c>/dev/tty</c> opens (a controlling terminal, EM-15). Windows: whether a console is attached.</summary>
    private static bool HasTerminal()
    {
        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
        {
            var fd = Libc.Open("/dev/tty", Libc.OpenReadWrite | Libc.OpenNoCtty | Libc.OpenCloseOnExec);
            if (fd < 0)
            {
                return false;
            }

            _ = Libc.Close(fd);
            return true;
        }

        return OperatingSystem.IsWindows() && Kernel32.GetConsoleWindow() != 0;
    }

    /// <summary>What stdin is (EM-17): <c>null-device</c>, <c>pipe</c>, <c>file</c> or <c>terminal</c>.</summary>
    private static string StdinKind()
    {
        if (OperatingSystem.IsLinux())
        {
            if (Libc.IsAtty(0) == 1)
            {
                return "terminal";
            }

            string? target;
            try
            {
                target = new FileInfo("/proc/self/fd/0").LinkTarget;
            }
            catch (IOException)
            {
                target = null;
            }

            return target switch
            {
                null or "/dev/null" => "null-device",
                _ when target.StartsWith("pipe:", StringComparison.Ordinal)
                    || target.StartsWith("socket:", StringComparison.Ordinal) => "pipe",
                _ => "file",
            };
        }

        if (OperatingSystem.IsMacOS())
        {
            return Libc.IsAtty(0) == 1 ? "terminal" : Console.IsInputRedirected ? "pipe" : "null-device";
        }

        return !Console.IsInputRedirected ? "terminal" : "pipe";
    }
}
