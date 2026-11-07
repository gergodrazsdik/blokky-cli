using System.Globalization;
using System.Runtime.Versioning;
using System.Text;
using Blokky.Testkit.Interop;

namespace Blokky.Testkit;

/// <summary>File access shared between concurrent testkit processes: appends and counters under an exclusive lock (TK-5, TK-19).
/// Every failure is an internal failure (124).</summary>
internal static class Files
{
    /// <summary>Appends the bytes in one write under an exclusive lock, so lines from concurrent processes never interleave.</summary>
    public static void AppendLocked(string path, ReadOnlySpan<byte> bytes, string what)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.OpenOrCreate, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete, 1);
            using (Lock(stream))
            {
                stream.Seek(0, SeekOrigin.End);
                stream.Write(bytes);
                stream.Flush();
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            throw TestkitException.Internal($"{what}: can't append to '{path}': {e.Message}");
        }
    }

    /// <summary>Increments the decimal counter in <paramref name="path"/> (0 if missing) under an exclusive lock (TK-19).</summary>
    public static long Increment(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete, 1);
            using (Lock(stream))
            {
                var buffer = new byte[64];
                var length = 0;
                int read;
                while (length < buffer.Length && (read = stream.Read(buffer, length, buffer.Length - length)) > 0)
                {
                    length += read;
                }

                var text = Encoding.UTF8.GetString(buffer, 0, length).Trim();
                long value = 0;
                if (text.Length > 0 && (!text.All(char.IsAsciiDigit)
                    || !long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value)))
                {
                    throw TestkitException.Internal($"state file '{path}' doesn't hold a decimal integer");
                }

                value++;
                var bytes = Encoding.UTF8.GetBytes(value.ToString(CultureInfo.InvariantCulture) + "\n");
                stream.SetLength(0);
                stream.Seek(0, SeekOrigin.Begin);
                stream.Write(bytes);
                stream.Flush();
                return value;
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            throw TestkitException.Internal($"can't update state file '{path}': {e.Message}");
        }
    }

    /// <summary>Creates or overwrites a file (no parent directories are created).</summary>
    public static void Write(string path, ReadOnlySpan<byte> bytes, bool append, string what)
    {
        try
        {
            var mode = append ? FileMode.Append : FileMode.Create;
            using var stream = new FileStream(path, mode, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete, 1);
            stream.Write(bytes);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            throw TestkitException.Internal($"{what}: can't write '{path}': {e.Message}");
        }
    }

    private static Unlocker Lock(FileStream stream)
    {
        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
        {
            var fd = (int)stream.SafeFileHandle.DangerousGetHandle();
            while (Libc.Flock(fd, Libc.LockEx) != 0)
            {
                if (System.Runtime.InteropServices.Marshal.GetLastPInvokeError() != Libc.EINTR)
                {
                    throw new IOException("flock failed");
                }
            }

            return UnixUnlocker(fd);
        }

        if (OperatingSystem.IsWindows())
        {
            // Windows (0.6): LockFile doesn't wait, so retry.
            for (var attempt = 0; ; attempt++)
            {
                try
                {
                    stream.Lock(0, long.MaxValue);
                    return WindowsUnlocker(stream);
                }
                catch (IOException) when (attempt < 10_000)
                {
                    Thread.Sleep(1);
                }
            }
        }

        return new Unlocker(() => { });
    }

    [SupportedOSPlatform("linux")]
    [SupportedOSPlatform("macos")]
    private static Unlocker UnixUnlocker(int fd) => new(() => Libc.Flock(fd, Libc.LockUn));

    [SupportedOSPlatform("windows")]
    private static Unlocker WindowsUnlocker(FileStream stream) => new(() => stream.Unlock(0, long.MaxValue));

    private sealed class Unlocker(Action release) : IDisposable
    {
        public void Dispose() => release();
    }
}
