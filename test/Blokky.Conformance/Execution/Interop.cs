using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Blokky.Conformance.Execution;

/// <summary>The few libc calls the runner needs (tech-stack.md §6: <c>LibraryImport</c> only).</summary>
internal static partial class Interop
{
    public const int SigTerm = 15;

    public const int SigKill = 9;

    public const int LockSh = 1;

    public const int LockUn = 8;

    public const int EINTR = 4;

    /// <summary>Sends <paramref name="signal"/> to <paramref name="pid"/>; the external stop is SIGTERM (CF-55).</summary>
    [UnsupportedOSPlatform("windows")]
    [LibraryImport("libc", EntryPoint = "kill", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    internal static partial int Kill(int pid, int signal);

    /// <summary>Creates a hard link: aliases resolve their <c>.testkit-args</c> next to the link (TK-6).</summary>
    [UnsupportedOSPlatform("windows")]
    [LibraryImport("libc", EntryPoint = "link", SetLastError = true, StringMarshalling = StringMarshalling.Utf8)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    internal static partial int Link(string existing, string link);

    /// <summary>Takes or releases an advisory lock: the testkit appends log lines under LOCK_EX (TK-5).</summary>
    [UnsupportedOSPlatform("windows")]
    [LibraryImport("libc", EntryPoint = "flock", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    internal static partial int Flock(int fd, int operation);
}
