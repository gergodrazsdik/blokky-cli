using System.Runtime.InteropServices;
using System.Runtime.Versioning;

[assembly: System.Runtime.CompilerServices.DisableRuntimeMarshalling]

namespace Blokky.Testkit.Interop;

/// <summary>libc calls the testkit needs on Unix (tech-stack.md §6). Constants are Linux values unless noted.</summary>
[SupportedOSPlatform("linux")]
[SupportedOSPlatform("macos")]
internal static unsafe partial class Libc
{
    private const string Lib = "libc";

    public const int EINTR = 4;
    public const int EAGAIN = 11;
    public const int ESRCH = 3;
    public const int EPERM = 1;

    public const int LockEx = 2;
    public const int LockUn = 8;

    public const int RlimitCore = 4;

    [LibraryImport(Lib, EntryPoint = "write", SetLastError = true)]
    public static partial nint Write(int fd, byte* buffer, nint count);

    [LibraryImport(Lib, EntryPoint = "read", SetLastError = true)]
    public static partial nint Read(int fd, byte* buffer, nint count);

    [LibraryImport(Lib, EntryPoint = "close", SetLastError = true)]
    public static partial int Close(int fd);

    [LibraryImport(Lib, EntryPoint = "open", SetLastError = true, StringMarshalling = StringMarshalling.Utf8)]
    public static partial int Open(string path, int flags);

    [LibraryImport(Lib, EntryPoint = "isatty")]
    public static partial int IsAtty(int fd);

    [LibraryImport(Lib, EntryPoint = "flock", SetLastError = true)]
    public static partial int Flock(int fd, int operation);

    [LibraryImport(Lib, EntryPoint = "kill", SetLastError = true)]
    public static partial int Kill(int pid, int signal);

    [LibraryImport(Lib, EntryPoint = "getpid")]
    public static partial int GetPid();

    [LibraryImport(Lib, EntryPoint = "getppid")]
    public static partial int GetPpid();

    [LibraryImport(Lib, EntryPoint = "getuid")]
    public static partial uint GetUid();

    [LibraryImport(Lib, EntryPoint = "geteuid")]
    public static partial uint GetEuid();

    [LibraryImport(Lib, EntryPoint = "signal", SetLastError = true)]
    public static partial nint Signal(int signal, nint handler);

    [LibraryImport(Lib, EntryPoint = "setrlimit", SetLastError = true)]
    public static partial int SetRlimit(int resource, RLimit* limit);

    [LibraryImport(Lib, EntryPoint = "realpath", SetLastError = true, StringMarshalling = StringMarshalling.Utf8)]
    public static partial nint RealPath(string path, nint resolved);

    [LibraryImport(Lib, EntryPoint = "free")]
    public static partial void Free(nint pointer);

    [LibraryImport(Lib, EntryPoint = "waitpid", SetLastError = true)]
    public static partial int WaitPid(int pid, int* status, int options);

    [LibraryImport(Lib, EntryPoint = "posix_spawn")]
    public static partial int PosixSpawn(int* pid, byte* path, void* fileActions, void* attributes, byte** argv, byte** envp);

    [LibraryImport(Lib, EntryPoint = "posix_spawnattr_init")]
    public static partial int SpawnAttrInit(void* attributes);

    [LibraryImport(Lib, EntryPoint = "posix_spawnattr_destroy")]
    public static partial int SpawnAttrDestroy(void* attributes);

    [LibraryImport(Lib, EntryPoint = "posix_spawnattr_setflags")]
    public static partial int SpawnAttrSetFlags(void* attributes, short flags);

    [LibraryImport(Lib, EntryPoint = "posix_spawnattr_setsigdefault")]
    public static partial int SpawnAttrSetSigDefault(void* attributes, void* signals);

    [LibraryImport(Lib, EntryPoint = "posix_spawnattr_setsigmask")]
    public static partial int SpawnAttrSetSigMask(void* attributes, void* signals);

    [LibraryImport(Lib, EntryPoint = "posix_spawn_file_actions_init")]
    public static partial int FileActionsInit(void* actions);

    [LibraryImport(Lib, EntryPoint = "posix_spawn_file_actions_destroy")]
    public static partial int FileActionsDestroy(void* actions);

    [LibraryImport(Lib, EntryPoint = "posix_spawn_file_actions_addopen", StringMarshalling = StringMarshalling.Utf8)]
    public static partial int FileActionsAddOpen(void* actions, int fd, string path, int flags, uint mode);

    [LibraryImport(Lib, EntryPoint = "sigfillset")]
    public static partial int SigFillSet(void* set);

    [LibraryImport(Lib, EntryPoint = "sigemptyset")]
    public static partial int SigEmptySet(void* set);

    [StructLayout(LayoutKind.Sequential)]
    public struct RLimit
    {
        public ulong Current;
        public ulong Maximum;
    }

    public static int OpenReadWrite => 2;
    public static int OpenReadOnly => 0;
    public static int OpenWriteOnly => 1;
    public static int OpenNoCtty => OperatingSystem.IsMacOS() ? 0x20000 : 0x100;
    public static int OpenCloseOnExec => OperatingSystem.IsMacOS() ? 0x1000000 : 0x80000;

    public static short SpawnSetSigDefault => 0x04;
    public static short SpawnSetSigMask => 0x08;
    public static short SpawnSetSid => OperatingSystem.IsMacOS() ? (short)0x0400 : (short)0x80;

    /// <summary>Signal numbers of the signals whose default action terminates the process (TK-21).</summary>
    public static int? SignalNumber(string name)
    {
        var mac = OperatingSystem.IsMacOS();
        return name switch
        {
            "SIGHUP" => 1,
            "SIGINT" => 2,
            "SIGQUIT" => 3,
            "SIGILL" => 4,
            "SIGTRAP" => 5,
            "SIGABRT" => 6,
            "SIGBUS" => mac ? 10 : 7,
            "SIGFPE" => 8,
            "SIGKILL" => 9,
            "SIGUSR1" => mac ? 30 : 10,
            "SIGSEGV" => 11,
            "SIGUSR2" => mac ? 31 : 12,
            "SIGPIPE" => 13,
            "SIGALRM" => 14,
            "SIGTERM" => 15,
            "SIGXCPU" => 24,
            "SIGXFSZ" => 25,
            "SIGVTALRM" => 26,
            "SIGPROF" => 27,
            "SIGSYS" => mac ? 12 : 31,
            _ => null,
        };
    }

    /// <summary>Writes all of <paramref name="bytes"/> to <paramref name="fd"/>; returns the errno on failure, 0 on success.</summary>
    public static int WriteAll(int fd, ReadOnlySpan<byte> bytes)
    {
        fixed (byte* start = bytes)
        {
            var offset = 0;
            while (offset < bytes.Length)
            {
                var written = Write(fd, start + offset, bytes.Length - offset);
                if (written < 0)
                {
                    var errno = Marshal.GetLastPInvokeError();
                    if (errno == EINTR)
                    {
                        continue;
                    }

                    if (errno == EAGAIN)
                    {
                        Thread.Sleep(1);
                        continue;
                    }

                    return errno;
                }

                offset += (int)written;
            }
        }

        return 0;
    }
}
