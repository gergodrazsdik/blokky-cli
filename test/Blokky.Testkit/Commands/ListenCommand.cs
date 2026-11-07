using System.Net;
using System.Net.Sockets;

namespace Blokky.Testkit.Commands;

/// <summary><c>listen</c> (TK-20): a loopback TCP listener that accepts and closes connections.</summary>
internal static class ListenCommand
{
    private const int CannotBind = 4;

    public static int Run(IReadOnlyList<Arg> args)
    {
        var parsed = Testkit.Prepare(
            new CommandSpec("listen").Value("--port").Value("--after").Value("--out").Value("--exit-after").Value("--exit"),
            args);
        parsed.NoPositionals();
        var port = parsed.GetText("--port") is { } p
            ? Grammar.Integer("--port", p, 1, 65535)
            : throw TestkitException.Usage("listen: --port is required");
        var after = parsed.GetText("--after") is { } a ? Grammar.Duration("--after", a) : TimeSpan.Zero;
        var exitAfter = parsed.GetText("--exit-after") is { } e ? Grammar.Duration("--exit-after", e) : (TimeSpan?)null;
        var exitCode = parsed.GetText("--exit") is { } c ? Grammar.ExitCode("--exit", c) : 0;
        var text = parsed.Get("--out");

        Thread.Sleep(after);

        var listeners = new List<Socket>();
        try
        {
            listeners.Add(Bind(IPAddress.Loopback, port));
        }
        catch (SocketException ex)
        {
            StdStream.Diagnostic($"listen: can't bind 127.0.0.1:{port}: {ex.SocketErrorCode}");
            return CannotBind;
        }

        if (Socket.OSSupportsIPv6)
        {
            try
            {
                listeners.Add(Bind(IPAddress.IPv6Loopback, port));
            }
            catch (SocketException ex) when (ex.SocketErrorCode is SocketError.AddressNotAvailable or SocketError.AddressFamilyNotSupported)
            {
                // No ::1 on this host: IPv4 only ("where available").
            }
            catch (SocketException ex)
            {
                StdStream.Diagnostic($"listen: can't bind [::1]:{port}: {ex.SocketErrorCode}");
                return CannotBind;
            }
        }

        foreach (var listener in listeners)
        {
            new Thread(() => AcceptLoop(listener)) { IsBackground = true }.Start();
        }

        if (text is { } line)
        {
            StdStream.Out.Write([.. line.Bytes, (byte)'\n']);
        }

        if (exitAfter is { } d)
        {
            Thread.Sleep(d);
            return exitCode;
        }

        Thread.Sleep(Timeout.Infinite);
        return 0;
    }

    private static Socket Bind(IPAddress address, int port)
    {
        var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
        try
        {
            socket.Bind(new IPEndPoint(address, port));
            socket.Listen(128);
            return socket;
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    private static void AcceptLoop(Socket listener)
    {
        while (true)
        {
            try
            {
                // Closed at once, nothing read or written.
                using var connection = listener.Accept();
            }
            catch (SocketException)
            {
                // A connection reset before accept; keep listening.
            }
        }
    }
}
