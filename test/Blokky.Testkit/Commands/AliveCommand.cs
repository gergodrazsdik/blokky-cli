using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using Blokky.Testkit.Interop;

namespace Blokky.Testkit.Commands;

/// <summary><c>alive</c> (TK-16): which recorded pids are still alive.</summary>
internal static class AliveCommand
{
    public static int Run(IReadOnlyList<Arg> args)
    {
        var parsed = Testkit.Prepare(new CommandSpec("alive"), args);
        var path = parsed.Single("FILE").Text;
        string[] lines;
        try
        {
            lines = File.ReadAllLines(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            throw TestkitException.Internal($"alive: can't read '{path}': {e.Message}");
        }

        var alive = new List<int>();
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            if (!line.All(char.IsAsciiDigit)
                || !int.TryParse(line, NumberStyles.None, CultureInfo.InvariantCulture, out var pid) || pid <= 0)
            {
                throw TestkitException.Internal($"alive: '{path}' holds a line that isn't a pid: '{line}'");
            }

            if (IsAlive(pid))
            {
                alive.Add(pid);
            }
        }

        StdStream.Out.Write(Json.Line(w =>
        {
            w.WriteStartArray();
            foreach (var pid in alive)
            {
                w.WriteNumberValue(pid);
            }

            w.WriteEndArray();
        }));
        return alive.Count == 0 ? 0 : 1;
    }

    private static bool IsAlive(int pid)
    {
        if (OperatingSystem.IsLinux())
        {
            // A zombie has exited: it only waits to be reaped, so it doesn't count as alive.
            try
            {
                var stat = File.ReadAllText($"/proc/{pid}/stat");
                var state = stat[(stat.LastIndexOf(')') + 2)..].FirstOrDefault();
                return state is not ('Z' or 'X' or 'x');
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentOutOfRangeException)
            {
                return false;
            }
        }

        if (OperatingSystem.IsMacOS())
        {
            return Libc.Kill(pid, 0) == 0 || Marshal.GetLastPInvokeError() == Libc.EPERM;
        }

        try
        {
            using var process = Process.GetProcessById(pid);
            return !process.HasExited;
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            // HasExited throws without access to the process: it exists.
            return e is not ArgumentException;
        }
    }
}
