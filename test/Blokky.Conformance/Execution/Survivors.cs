using System.Globalization;
using System.Text;

namespace Blokky.Conformance.Execution;

/// <summary>
/// Processes a case left behind. Every process of a case inherits its <c>TESTKIT_LOG</c> (CF-57), which names the case's own
/// log file, so the entry <c>TESTKIT_LOG=&lt;path&gt;</c> marks it; the runner finds marked processes in
/// <c>/proc/*/environ</c> (Linux). A process that clears its environment or drops the variable isn't found, but it can't
/// write to the case's log either.
/// </summary>
internal static class Survivors
{
    /// <summary>The pause between the quick rescans that confirm nothing is left.</summary>
    private static readonly TimeSpan RescanInterval = TimeSpan.FromMilliseconds(20);

    /// <summary>Scans per round (<see cref="ScanAsync"/>).</summary>
    private const int Scans = 3;

    /// <summary>The environment entry that marks a case's processes.</summary>
    public static string Marker(string logPath) => "TESTKIT_LOG=" + logPath;

    /// <summary>Live processes carrying <paramref name="marker"/>, other than the runner; empty where there's no <c>/proc</c>.
    /// Processes whose environment can't be read (gone, another user's, a zombie's) are ignored.</summary>
    public static IReadOnlyList<int> Find(string marker)
    {
        ArgumentNullException.ThrowIfNull(marker);
        if (!OperatingSystem.IsLinux())
        {
            return [];
        }

        var entry = Encoding.UTF8.GetBytes(marker);
        var self = Environment.ProcessId;
        var found = new List<int>();
        IEnumerable<string> dirs;
        try
        {
            dirs = Directory.EnumerateDirectories("/proc");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }

        foreach (var dir in dirs)
        {
            if (!int.TryParse(Path.GetFileName(dir), NumberStyles.None, CultureInfo.InvariantCulture, out var pid) || pid == self)
            {
                continue;
            }

            byte[] environ;
            try
            {
                environ = File.ReadAllBytes(Path.Combine(dir, "environ"));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            if (HasEntry(environ, entry))
            {
                found.Add(pid);
            }
        }

        return found;
    }

    /// <summary>Whether the NUL-separated <paramref name="environ"/> holds exactly <paramref name="entry"/>.</summary>
    internal static bool HasEntry(ReadOnlySpan<byte> environ, ReadOnlySpan<byte> entry)
    {
        foreach (var range in environ.Split((byte)0))
        {
            if (environ[range].SequenceEqual(entry))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The union of <see cref="Scans"/> scans, <see cref="RescanInterval"/> apart, each passed to <paramref name="onFound"/>.
    /// One scan isn't enough: a process can be unreadable for a moment while it execs, and one that forks and exits between
    /// the directory listing and the read of its <c>environ</c> leaves a child the listing missed.
    /// </summary>
    private static async Task<IReadOnlyList<int>> ScanAsync(string marker, Action<IReadOnlyList<int>>? onFound,
        CancellationToken cancellationToken)
    {
        var all = new List<int>();
        for (var scan = 0; scan < Scans; scan++)
        {
            if (scan > 0)
            {
                await Task.Delay(RescanInterval, TimeProvider.System, cancellationToken).ConfigureAwait(false);
            }

            var pids = Find(marker);
            onFound?.Invoke(pids);
            all.AddRange(pids.Where(p => !all.Contains(p)));
        }

        return all;
    }

    /// <summary>
    /// Waits until the case's processes have had time to act after <c>blokky</c> exited, and returns the ones seen alive
    /// at its end. When the first scans find none, nothing can log any more and it returns at once; otherwise it waits out
    /// <paramref name="settle"/>, so a process that starts late and logs within it is counted.
    /// </summary>
    public static async Task<IReadOnlyList<int>> SettleAsync(string marker, TimeSpan settle, CancellationToken cancellationToken)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        if ((await ScanAsync(marker, null, cancellationToken).ConfigureAwait(false)).Count == 0)
        {
            return [];
        }

        var rest = settle - clock.Elapsed;
        if (rest > TimeSpan.Zero)
        {
            await Task.Delay(rest, TimeProvider.System, cancellationToken).ConfigureAwait(false);
        }

        return await ScanAsync(marker, null, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// SIGKILLs every process carrying <paramref name="marker"/>, again until a full round of scans finds none (a dying
    /// parent can fork once more), for at most <paramref name="limit"/>. Killed processes are reaped by their parents or
    /// init: the runner's only children are the processes <see cref="ProcessRunner"/> started and waited for. Returns the
    /// pids it killed.
    /// </summary>
    public static async Task<IReadOnlyList<int>> KillAsync(string marker, TimeSpan limit)
    {
        var killed = new List<int>();
        if (OperatingSystem.IsWindows())
        {
            return killed;
        }

        var clock = System.Diagnostics.Stopwatch.StartNew();
        void KillAll(IReadOnlyList<int> pids)
        {
            if (OperatingSystem.IsWindows())
            {
                return;
            }

            foreach (var pid in pids)
            {
                if (Interop.Kill(pid, Interop.SigKill) == 0 && !killed.Contains(pid))
                {
                    killed.Add(pid);
                }
            }
        }

        while (clock.Elapsed < limit && (await ScanAsync(marker, KillAll, CancellationToken.None).ConfigureAwait(false)).Count > 0)
        {
            // Killed this round; scan again until a round finds nothing.
        }

        return killed;
    }
}
