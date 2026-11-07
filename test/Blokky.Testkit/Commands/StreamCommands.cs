using System.Runtime.InteropServices;
using Blokky.Testkit.Interop;

namespace Blokky.Testkit.Commands;

/// <summary><c>cat</c> (TK-11): stdin to stdout or stderr, byte for byte.</summary>
internal static class StreamCommands
{
    public static unsafe int Cat(IReadOnlyList<Arg> args)
    {
        var parsed = Testkit.Prepare(new CommandSpec("cat").Value("--to"), args);
        parsed.NoPositionals();
        var target = parsed.GetText("--to") switch
        {
            null or "stdout" => StdStream.Out,
            "stderr" => StdStream.Err,
            var other => throw TestkitException.Usage($"cat: --to expects stdout or stderr: '{other}'"),
        };

        var buffer = new byte[64 * 1024];
        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
        {
            // Never wait for a terminal (TK-11).
            if (Libc.IsAtty(0) == 1)
            {
                return 0;
            }

            fixed (byte* start = buffer)
            {
                while (true)
                {
                    var read = Libc.Read(0, start, buffer.Length);
                    if (read == 0)
                    {
                        return 0;
                    }

                    if (read < 0)
                    {
                        var errno = Marshal.GetLastPInvokeError();
                        if (errno is Libc.EINTR or Libc.EAGAIN)
                        {
                            continue;
                        }

                        // A closed stdin (EBADF) reads as empty.
                        return 0;
                    }

                    target.Write(buffer.AsSpan(0, (int)read));
                }
            }
        }

        if (!Console.IsInputRedirected)
        {
            return 0;
        }

        using var stdin = Console.OpenStandardInput();
        int count;
        while ((count = stdin.Read(buffer, 0, buffer.Length)) > 0)
        {
            target.Write(buffer.AsSpan(0, count));
        }

        return 0;
    }
}
