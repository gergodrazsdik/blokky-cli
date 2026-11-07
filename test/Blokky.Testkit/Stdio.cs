using System.Runtime.InteropServices;
using Blokky.Testkit.Interop;

namespace Blokky.Testkit;

/// <summary>Unbuffered stdout or stderr: every write goes to the OS at once (TK-4), and the stream can be closed (TK-10).</summary>
internal sealed class StdStream(int fd)
{
    private Stream? _stream;

    public bool Closed { get; private set; }

    public static StdStream Out { get; } = new(1);

    public static StdStream Err { get; } = new(2);

    /// <summary>Writes all bytes; a failed write or a write after close is an internal failure (124).</summary>
    public void Write(ReadOnlySpan<byte> bytes)
    {
        if (Closed)
        {
            throw TestkitException.Internal($"write to closed {Name}");
        }

        if (bytes.IsEmpty)
        {
            return;
        }

        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
        {
            var errno = Libc.WriteAll(fd, bytes);
            if (errno != 0)
            {
                throw TestkitException.Internal($"write to {Name} failed: {Marshal.GetPInvokeErrorMessage(errno)}");
            }

            return;
        }

        try
        {
            _stream ??= fd == 1 ? Console.OpenStandardOutput() : Console.OpenStandardError();
            _stream.Write(bytes);
            _stream.Flush();
        }
        catch (IOException e)
        {
            throw TestkitException.Internal($"write to {Name} failed: {e.Message}");
        }
    }

    /// <summary>Closes the stream's descriptor, so a reader sees end of input if no one else holds it (TK-10).</summary>
    public void Close()
    {
        if (Closed)
        {
            return;
        }

        Closed = true;
        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
        {
            _ = Libc.Close(fd);
        }
        else if (OperatingSystem.IsWindows())
        {
            _stream?.Dispose();
            _ = Kernel32.CloseHandle(Kernel32.GetStdHandle(fd == 1 ? Kernel32.StdOutputHandle : Kernel32.StdErrorHandle));
        }
    }

    private string Name => fd == 1 ? "stdout" : "stderr";

    /// <summary>Writes a <c>blokky-testkit:</c> line to stderr, ignoring failures (TK-4).</summary>
    public static void Diagnostic(string message)
    {
        if (Err.Closed)
        {
            return;
        }

        try
        {
            Err.Write(System.Text.Encoding.UTF8.GetBytes($"blokky-testkit: {message.ReplaceLineEndings(" ")}\n"));
        }
        catch (TestkitException)
        {
            // Nowhere left to report it.
        }
    }
}
