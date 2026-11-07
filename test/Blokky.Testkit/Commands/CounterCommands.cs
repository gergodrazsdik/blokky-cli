using System.Globalization;

namespace Blokky.Testkit.Commands;

/// <summary><c>count</c> and <c>flaky</c> (TK-19): a counter in a state file, incremented under an exclusive lock.</summary>
internal static class CounterCommands
{
    public static int Count(IReadOnlyList<Arg> args)
    {
        var parsed = Testkit.Prepare(new CommandSpec("count").Value("--state"), args);
        parsed.NoPositionals();
        Increment(parsed);
        return 0;
    }

    public static int Flaky(IReadOnlyList<Arg> args)
    {
        var parsed = Testkit.Prepare(new CommandSpec("flaky").Value("--state").Value("--fail-times").Value("--code"), args);
        parsed.NoPositionals();
        var failTimes = parsed.GetText("--fail-times") is { } n
            ? Grammar.Integer("--fail-times", n, 0, int.MaxValue)
            : throw TestkitException.Usage("flaky: --fail-times is required");
        var code = parsed.GetText("--code") is { } c ? Grammar.ExitCode("--code", c) : 1;
        return Increment(parsed) <= failTimes ? code : 0;
    }

    private static long Increment(ParsedArgs parsed)
    {
        var state = parsed.GetText("--state") ?? throw TestkitException.Usage($"{parsed.Command}: --state is required");
        var value = Files.Increment(state);
        StdStream.Out.Write(System.Text.Encoding.UTF8.GetBytes(value.ToString(CultureInfo.InvariantCulture) + "\n"));
        return value;
    }
}
