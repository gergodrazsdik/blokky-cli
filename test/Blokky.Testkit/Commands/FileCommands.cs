namespace Blokky.Testkit.Commands;

/// <summary><c>write-file</c>, <c>read-file</c> (TK-18), <c>symlink</c> (TK-22) and <c>chmod</c> (TK-23).</summary>
internal static class FileCommands
{
    public static int WriteFile(IReadOnlyList<Arg> args)
    {
        var parsed = Testkit.Prepare(
            new CommandSpec("write-file").Value("--text").Value("--hex").Flag("--append").Value("--newline"),
            args);
        var path = parsed.Single("PATH").Text;
        var newline = Grammar.Newline(parsed.GetText("--newline"));
        byte[] bytes;
        if (parsed.Get("--text") is { } text && !parsed.Has("--hex"))
        {
            bytes = [.. text.Bytes, .. newline];
        }
        else if (parsed.GetText("--hex") is { } hex && !parsed.Has("--text"))
        {
            bytes = Grammar.Hex("--hex", hex);
        }
        else
        {
            throw TestkitException.Usage("write-file: expected exactly one of --text and --hex");
        }

        Files.Write(path, bytes, parsed.Has("--append"), "write-file");
        return 0;
    }

    public static int ReadFile(IReadOnlyList<Arg> args)
    {
        var parsed = Testkit.Prepare(new CommandSpec("read-file"), args);
        var path = parsed.Single("PATH").Text;
        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(path);
        }
        catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException)
        {
            return 3;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            throw TestkitException.Internal($"read-file: can't read '{path}': {e.Message}");
        }

        StdStream.Out.Write(bytes);
        return 0;
    }

    public static int Symlink(IReadOnlyList<Arg> args)
    {
        var parsed = Testkit.Prepare(new CommandSpec("symlink"), args);
        var (target, link) = parsed.Pair("TARGET", "LINK");
        if (OperatingSystem.IsWindows())
        {
            throw TestkitException.NotSupported("symlink");
        }

        try
        {
            File.CreateSymbolicLink(link.Text, target.Text);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            throw TestkitException.Internal($"symlink: can't create '{link.Text}': {e.Message}");
        }

        return 0;
    }

    public static int Chmod(IReadOnlyList<Arg> args)
    {
        var parsed = Testkit.Prepare(new CommandSpec("chmod"), args);
        var (modeArg, path) = parsed.Pair("MODE", "PATH");
        var mode = modeArg.Text;
        if (mode.Length is 0 or > 5 || !mode.All(c => c is >= '0' and <= '7'))
        {
            throw TestkitException.Usage($"chmod: MODE must be an octal number up to 07777: '{mode}'");
        }

        var bits = Convert.ToInt32(mode, 8);
        if (bits > 0xFFF)
        {
            throw TestkitException.Usage($"chmod: MODE must be an octal number up to 07777: '{mode}'");
        }

        if (OperatingSystem.IsWindows())
        {
            throw TestkitException.NotSupported("chmod");
        }

        try
        {
            File.SetUnixFileMode(path.Text, (UnixFileMode)bits);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            throw TestkitException.Internal($"chmod: can't change '{path.Text}': {e.Message}");
        }

        return 0;
    }
}
