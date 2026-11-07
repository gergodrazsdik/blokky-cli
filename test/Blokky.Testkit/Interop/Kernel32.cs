using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Blokky.Testkit.Interop;

/// <summary>Windows calls the testkit needs (Windows support lands with 0.6; these keep the build honest).</summary>
[SupportedOSPlatform("windows")]
internal static unsafe partial class Kernel32
{
    public const int StdOutputHandle = -11;
    public const int StdErrorHandle = -12;

    [LibraryImport("kernel32.dll", SetLastError = true)]
    public static partial nint GetStdHandle(int handle);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool CloseHandle(nint handle);

    [LibraryImport("kernel32.dll")]
    public static partial char* GetCommandLineW();

    [LibraryImport("kernel32.dll")]
    public static partial nint GetConsoleWindow();

    [LibraryImport("kernel32.dll")]
    public static partial nint GetCurrentProcess();

    [LibraryImport("ntdll.dll")]
    public static partial int NtQueryInformationProcess(
        nint process, int infoClass, ProcessBasicInformation* info, int length, int* returnLength);

    [StructLayout(LayoutKind.Sequential)]
    public struct ProcessBasicInformation
    {
        public nint ExitStatus;
        public nint PebBaseAddress;
        public nint AffinityMask;
        public nint BasePriority;
        public nint UniqueProcessId;
        public nint InheritedFromUniqueProcessId;
    }

    public static int ParentProcessId()
    {
        ProcessBasicInformation info;
        int length;
        return NtQueryInformationProcess(GetCurrentProcess(), 0, &info, sizeof(ProcessBasicInformation), &length) == 0
            ? (int)info.InheritedFromUniqueProcessId
            : 0;
    }
}
