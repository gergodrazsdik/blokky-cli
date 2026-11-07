using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using Blokky.Testkit.Interop;

namespace Blokky.Testkit.Commands;

/// <summary><c>spawn</c> (TK-15): a tree of descendants that hold the output open or try to escape.
/// Children start with <c>posix_spawn</c>; the testkit never calls <c>fork()</c> (docs/design/testkit.md §2).</summary>
internal static class SpawnCommand
{
    public static int Run(IReadOnlyList<Arg> args)
    {
        var parsed = Testkit.Prepare(
            new CommandSpec("spawn")
                .Value("--depth")
                .Value("--count")
                .Flag("--release-output")
                .Value("--escape")
                .Value("--pids")
                .Flag("--wait"),
            args);
        if (parsed.Positionals.Count > 0)
        {
            throw TestkitException.Usage($"spawn: unexpected argument '{parsed.Positionals[0].Text}' (the command goes after --)");
        }

        if (parsed.Rest is not { Count: > 0 } command)
        {
            throw TestkitException.Usage("spawn: expected -- COMMAND...");
        }

        var depth = parsed.GetText("--depth") is { } d ? Grammar.Integer("--depth", d, 1, 100) : 1;
        var count = parsed.GetText("--count") is { } c ? Grammar.Integer("--count", c, 1, 1000) : 1;
        var escape = parsed.GetText("--escape");
        if (escape is not (null or "setsid" or "double-fork" or "breakaway"))
        {
            throw TestkitException.Usage($"spawn: --escape expects setsid, double-fork or breakaway: '{escape}'");
        }

        if (OperatingSystem.IsWindows())
        {
            // Windows spawn (job breakaway) comes with 0.6.
            throw TestkitException.NotSupported("spawn");
        }

        if (escape != "breakaway" && (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS()))
        {
            return RunUnix(parsed, command, depth, count, escape);
        }

        throw TestkitException.NotSupported($"spawn --escape {escape}");
    }

    [SupportedOSPlatform("linux")]
    [SupportedOSPlatform("macos")]
    private static int RunUnix(ParsedArgs parsed, IReadOnlyList<Arg> command, int depth, int count, string? escape)
    {
        var releaseOutput = parsed.Has("--release-output");
        var pidsFile = parsed.GetText("--pids");
        var wait = parsed.Has("--wait");

        // Each child is a spawn of the same shape one level less deep; the leaves run COMMAND.
        var shared = new List<Arg>();
        if (releaseOutput)
        {
            shared.Add(Arg.FromText("--release-output"));
        }

        if (pidsFile is not null)
        {
            shared.AddRange([Arg.FromText("--pids"), parsed.Get("--pids")!.Value]);
        }

        List<Arg> childArgs = depth > 1
            ? [Arg.FromText("spawn"), Arg.FromText("--depth"), Num(depth - 1), Arg.FromText("--count"), Num(count),
                .. shared, .. wait ? [Arg.FromText("--wait")] : Array.Empty<Arg>(), Arg.FromText("--"), .. command]
            : [.. command];

        // double-fork: the direct child is an intermediate that starts the real child in a new session and exits at once,
        // so the real child is reparented away from the testkit (a daemon). The intermediate logs its own line (TK-5).
        var setsid = escape == "setsid";
        if (escape == "double-fork")
        {
            childArgs =
            [
                Arg.FromText("spawn"), Arg.FromText("--escape"), Arg.FromText("setsid"), .. shared, Arg.FromText("--"), .. childArgs,
            ];
        }

        var testkit = Invocation.TestkitPath();
        var pids = new List<int>();
        for (var i = 0; i < count; i++)
        {
            var pid = Spawn(testkit, childArgs, releaseOutput, setsid);
            pids.Add(pid);
            if (pidsFile is not null)
            {
                Files.AppendLocked(pidsFile, Encoding.UTF8.GetBytes(pid.ToString(CultureInfo.InvariantCulture) + "\n"), "--pids");
            }
        }

        if (!wait)
        {
            return 0;
        }

        var result = 0;
        foreach (var pid in pids)
        {
            var status = WaitFor(pid);
            if (result == 0 && status != 0)
            {
                result = status;
            }
        }

        return result;
    }

    private static Arg Num(int value) => Arg.FromText(value.ToString(CultureInfo.InvariantCulture));

    [SupportedOSPlatform("linux")]
    [SupportedOSPlatform("macos")]
    private static unsafe int Spawn(string program, List<Arg> args, bool nullStdio, bool setsid)
    {
        var argv = new List<byte[]> { Encoding.UTF8.GetBytes(program) };
        argv.AddRange(args.Select(a => a.Bytes));
        var envp = Environ();

        // Opaque libc types, allocated natively and used only through libc's accessors (process-control.md).
        var attributes = NativeMemory.AllocZeroed(1024);
        var actions = NativeMemory.AllocZeroed(1024);
        var signals = NativeMemory.AllocZeroed(256);
        var handles = new List<GCHandle>();
        try
        {
            _ = Libc.SpawnAttrInit(attributes);
            _ = Libc.FileActionsInit(actions);

            // Default dispositions and an empty mask: the runtime ignores SIGPIPE, and an ignored disposition survives exec.
            var flags = (short)(Libc.SpawnSetSigDefault | Libc.SpawnSetSigMask | (setsid ? Libc.SpawnSetSid : 0));
            _ = Libc.SigFillSet(signals);
            _ = Libc.SpawnAttrSetSigDefault(attributes, signals);
            _ = Libc.SigEmptySet(signals);
            _ = Libc.SpawnAttrSetSigMask(attributes, signals);
            _ = Libc.SpawnAttrSetFlags(attributes, flags);

            if (nullStdio)
            {
                _ = Libc.FileActionsAddOpen(actions, 0, "/dev/null", Libc.OpenReadOnly, 0);
                _ = Libc.FileActionsAddOpen(actions, 1, "/dev/null", Libc.OpenWriteOnly, 0);
                _ = Libc.FileActionsAddOpen(actions, 2, "/dev/null", Libc.OpenWriteOnly, 0);
            }

            var argvPointers = Pin(argv, handles);
            var envPointers = Pin(envp, handles);
            int pid;
            int error;
            fixed (byte** argvStart = argvPointers)
            fixed (byte** envStart = envPointers)
            {
                error = Libc.PosixSpawn(&pid, argvStart[0], actions, attributes, argvStart, envStart);
            }

            if (error != 0)
            {
                throw TestkitException.Internal($"spawn: posix_spawn failed: {Marshal.GetPInvokeErrorMessage(error)}");
            }

            return pid;
        }
        finally
        {
            _ = Libc.FileActionsDestroy(actions);
            _ = Libc.SpawnAttrDestroy(attributes);
            NativeMemory.Free(attributes);
            NativeMemory.Free(actions);
            NativeMemory.Free(signals);
            foreach (var handle in handles)
            {
                handle.Free();
            }
        }
    }

    /// <summary>NUL-terminated copies, pinned, plus the terminating null pointer.</summary>
    private static unsafe byte*[] Pin(List<byte[]> strings, List<GCHandle> handles)
    {
        var pointers = new byte*[strings.Count + 1];
        for (var i = 0; i < strings.Count; i++)
        {
            var terminated = new byte[strings[i].Length + 1];
            strings[i].CopyTo(terminated, 0);
            var handle = GCHandle.Alloc(terminated, GCHandleType.Pinned);
            handles.Add(handle);
            pointers[i] = (byte*)handle.AddrOfPinnedObject();
        }

        return pointers;
    }

    /// <summary>The environment as exact bytes (Linux: <c>/proc/self/environ</c>), else from the BCL.</summary>
    private static List<byte[]> Environ()
    {
        if (OperatingSystem.IsLinux())
        {
            try
            {
                var bytes = File.ReadAllBytes("/proc/self/environ");
                var result = new List<byte[]>();
                var span = bytes.AsSpan();
                foreach (var range in span.Split((byte)0))
                {
                    if (!span[range].IsEmpty)
                    {
                        result.Add(span[range].ToArray());
                    }
                }

                return result;
            }
            catch (IOException)
            {
                // Fall back to the BCL's copy.
            }
        }

        var variables = new List<byte[]>();
        foreach (System.Collections.DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            variables.Add(Encoding.UTF8.GetBytes($"{entry.Key}={entry.Value}"));
        }

        return variables;
    }

    /// <summary>Waits for a child: its exit code, or 128 + the signal that ended it.</summary>
    [SupportedOSPlatform("linux")]
    [SupportedOSPlatform("macos")]
    private static unsafe int WaitFor(int pid)
    {
        int status;
        while (Libc.WaitPid(pid, &status, 0) < 0)
        {
            if (Marshal.GetLastPInvokeError() != Libc.EINTR)
            {
                throw TestkitException.Internal($"spawn: waitpid({pid}) failed");
            }
        }

        var signal = status & 0x7f;
        return signal == 0 ? (status >> 8) & 0xff : 128 + signal;
    }
}
