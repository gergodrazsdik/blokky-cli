using System.Text;
using Blokky.Testkit.Interop;

namespace Blokky.Testkit;

/// <summary>How the testkit was invoked: its name, its arguments after alias expansion (TK-6), and the invocation log (TK-5).</summary>
internal static class Invocation
{
    public const string ProgramName = "blokky-testkit";

    /// <summary>The invoked name: the basename of argv[0] without <c>.exe</c>. A symlink's name is kept (TK-6).</summary>
    public static string InvokedName()
    {
        var argv0 = RawCommandLine() is { Count: > 0 } raw ? Encoding.UTF8.GetString(raw[0]) : Environment.GetCommandLineArgs()[0];
        var name = Path.GetFileName(argv0);
        if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            name = name[..^4];
        }

        return name;
    }

    /// <summary>The directory of the resolved executable: a symlink is resolved for the directory (TK-6).</summary>
    public static string ExecutableDirectory() =>
        Path.GetDirectoryName(Environment.ProcessPath) ?? throw TestkitException.Internal("can't find the testkit's own path");

    /// <summary>The path of <c>blokky-testkit</c> itself, for the processes <c>spawn</c> starts (TK-15).</summary>
    public static string TestkitPath()
    {
        var own = Path.Combine(ExecutableDirectory(), ProgramName + (OperatingSystem.IsWindows() ? ".exe" : ""));
        return File.Exists(own) ? own : Environment.ProcessPath!;
    }

    /// <summary>The arguments after the program name, as exact bytes where the OS gives them (TK-3).</summary>
    public static List<Arg> CommandLineArgs(string[] args)
    {
        if (RawCommandLine() is { } raw && raw.Count == args.Length + 1)
        {
            return [.. raw.Skip(1).Select(Arg.FromBytes)];
        }

        return [.. args.Select(Arg.FromText)];
    }

    /// <summary>Alias arguments from <c>&lt;name&gt;.testkit-args</c> next to the executable: UTF-8, one per line (TK-6).</summary>
    public static List<Arg> AliasArgs(string name)
    {
        var path = Path.Combine(ExecutableDirectory(), name + ".testkit-args");
        byte[] content;
        try
        {
            content = File.ReadAllBytes(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            throw TestkitException.Usage($"alias '{name}': can't read '{path}': {e.Message}");
        }

        // One argument per line, LF-separated; a final LF doesn't start another argument.
        var span = content.AsSpan();
        if (span.Length > 0 && span[^1] == (byte)'\n')
        {
            span = span[..^1];
        }

        var result = new List<Arg>();
        if (content.Length == 0)
        {
            return result;
        }

        foreach (var range in span.Split((byte)'\n'))
        {
            result.Add(Arg.FromBytes(span[range].ToArray()));
        }

        return result;
    }

    /// <summary>Appends this invocation's line to <c>TESTKIT_LOG</c>, if set (TK-5).</summary>
    public static void Log(string name, IReadOnlyList<Arg> argv)
    {
        var path = Environment.GetEnvironmentVariable("TESTKIT_LOG");
        if (string.IsNullOrEmpty(path))
        {
            return;
        }

        string cwd;
        try
        {
            cwd = Directory.GetCurrentDirectory();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            throw TestkitException.Internal($"invocation log: no working directory: {e.Message}");
        }

        var line = Json.Line(w =>
        {
            w.WriteStartObject();
            w.WriteString("name", name);
            w.WritePropertyName("argv");
            Json.StringArray(w, argv.Select(a => a.Text));
            w.WriteNumber("pid", Environment.ProcessId);
            w.WriteNumber("ppid", ParentProcessId());
            w.WriteString("cwd", cwd);
            w.WriteEndObject();
        });
        Files.AppendLocked(path, line, "invocation log");
    }

    public static int ParentProcessId()
    {
        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
        {
            return Libc.GetPpid();
        }

        return OperatingSystem.IsWindows() ? Kernel32.ParentProcessId() : 0;
    }

    /// <summary>argv as exact bytes from <c>/proc/self/cmdline</c> (Linux), or null.</summary>
    private static List<byte[]>? RawCommandLine()
    {
        if (!OperatingSystem.IsLinux())
        {
            return null;
        }

        try
        {
            var bytes = File.ReadAllBytes("/proc/self/cmdline");
            var span = bytes.AsSpan();
            if (span.Length > 0 && span[^1] == 0)
            {
                span = span[..^1];
            }

            var result = new List<byte[]>();
            foreach (var range in span.Split((byte)0))
            {
                result.Add(span[range].ToArray());
            }

            return result;
        }
        catch (IOException)
        {
            return null;
        }
    }
}
