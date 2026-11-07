namespace Blokky.Testkit.Commands;

/// <summary><c>print</c> (TK-10): ordered writes to stdout and stderr, then exit or hang.</summary>
internal static class PrintCommand
{
    private const int FillLine = 64;

    public static int Run(IReadOnlyList<Arg> args)
    {
        var spec = new CommandSpec("print")
            .Value("--out", repeatable: true)
            .Value("--err", repeatable: true)
            .Value("--out-hex", repeatable: true)
            .Value("--err-hex", repeatable: true)
            .Value("--out-fill", repeatable: true)
            .Value("--err-fill", repeatable: true)
            .Value("--sleep", repeatable: true)
            .Flag("--close-out", repeatable: true)
            .Flag("--close-err", repeatable: true)
            .Value("--newline")
            .Value("--exit")
            .Flag("--hang");
        var parsed = spec.Parse(args);
        parsed.NoPositionals();

        // Validate everything before the first write, so a usage error never follows partial output.
        var newline = Grammar.Newline(parsed.GetText("--newline"));
        if (parsed.Has("--exit") && parsed.Has("--hang"))
        {
            throw TestkitException.Usage("print: --exit and --hang exclude each other");
        }

        var exitCode = parsed.GetText("--exit") is { } code ? Grammar.ExitCode("--exit", code) : 0;
        var operations = new List<Action>();
        foreach (var option in parsed.Options)
        {
            var value = option.Value;
            operations.Add(option.Name switch
            {
                "--out" => Text(StdStream.Out, value!.Value.Bytes, newline),
                "--err" => Text(StdStream.Err, value!.Value.Bytes, newline),
                "--out-hex" => Bytes(StdStream.Out, Grammar.Hex(option.Name, value!.Value.Text)),
                "--err-hex" => Bytes(StdStream.Err, Grammar.Hex(option.Name, value!.Value.Text)),
                "--out-fill" => Fill(StdStream.Out, Grammar.Size(option.Name, value!.Value.Text)),
                "--err-fill" => Fill(StdStream.Err, Grammar.Size(option.Name, value!.Value.Text)),
                "--sleep" => Sleep(Grammar.Duration(option.Name, value!.Value.Text)),
                "--close-out" => StdStream.Out.Close,
                "--close-err" => StdStream.Err.Close,
                _ => Nothing,
            });
        }

        Testkit.ApplyCommon(parsed);
        foreach (var operation in operations)
        {
            operation();
        }

        if (parsed.Has("--hang"))
        {
            Thread.Sleep(Timeout.Infinite);
        }

        return exitCode;
    }

    private static Action Text(StdStream stream, byte[] text, byte[] newline) => () =>
    {
        // One write per operation, so a merged pipe sees whole operations in order (TK-4).
        var bytes = new byte[text.Length + newline.Length];
        text.CopyTo(bytes, 0);
        newline.CopyTo(bytes, text.Length);
        stream.Write(bytes);
    };

    /// <summary>The common options (TK-7) aren't operations.</summary>
    private static void Nothing()
    {
    }

    private static Action Bytes(StdStream stream, byte[] bytes) => () => stream.Write(bytes);

    private static Action Sleep(TimeSpan duration) => () => Thread.Sleep(duration);

    /// <summary>SIZE bytes of 63 <c>x</c> and an LF, repeated, the last line cut at SIZE.</summary>
    private static Action Fill(StdStream stream, long size) => () =>
    {
        var chunk = new byte[FillLine * 1024];
        for (var i = 0; i < chunk.Length; i++)
        {
            chunk[i] = i % FillLine == FillLine - 1 ? (byte)'\n' : (byte)'x';
        }

        var left = size;
        while (left > 0)
        {
            var count = (int)Math.Min(left, chunk.Length);
            stream.Write(chunk.AsSpan(0, count));
            left -= count;
        }
    };
}
