using System.Text;

namespace Blokky.Testkit;

/// <summary>One argument: its text and, where the OS gives them, its exact bytes (TK-3).</summary>
internal readonly record struct Arg(string Text, byte[] Bytes)
{
    public static Arg FromText(string text) => new(text, Encoding.UTF8.GetBytes(text));

    public static Arg FromBytes(byte[] bytes) => new(Encoding.UTF8.GetString(bytes), bytes);

    public override string ToString() => Text;
}

/// <summary>An option as given: its name and value (null for a flag).</summary>
internal readonly record struct Option(string Name, Arg? Value);

/// <summary>What a command accepts: option name to whether it takes a value, and which may repeat.</summary>
internal sealed class CommandSpec(string name)
{
    private readonly Dictionary<string, (bool TakesValue, bool Repeatable)> _options = new(StringComparer.Ordinal)
    {
        // Common options (TK-7).
        ["--pid-file"] = (true, false),
        ["--cwd-check"] = (true, false),
    };

    public string Name { get; } = name;

    public CommandSpec Flag(string option, bool repeatable = false)
    {
        _options[option] = (false, repeatable);
        return this;
    }

    public CommandSpec Value(string option, bool repeatable = false)
    {
        _options[option] = (true, repeatable);
        return this;
    }

    /// <summary>Parses the arguments after the command (TK-1): options anywhere, value in the next argument,
    /// <c>--</c> ends options.</summary>
    public ParsedArgs Parse(IReadOnlyList<Arg> args)
    {
        var options = new List<Option>();
        var positionals = new List<Arg>();
        List<Arg>? rest = null;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < args.Count; i++)
        {
            var text = args[i].Text;
            if (rest is not null)
            {
                rest.Add(args[i]);
            }
            else if (text == "--")
            {
                rest = [];
            }
            else if (text.StartsWith("--", StringComparison.Ordinal))
            {
                if (!_options.TryGetValue(text, out var spec))
                {
                    throw TestkitException.Usage($"{Name}: unknown option {text}");
                }

                if (!spec.Repeatable && !seen.Add(text))
                {
                    throw TestkitException.Usage($"{Name}: option {text} given more than once");
                }

                if (spec.TakesValue)
                {
                    if (i + 1 >= args.Count)
                    {
                        throw TestkitException.Usage($"{Name}: option {text} needs a value");
                    }

                    options.Add(new Option(text, args[++i]));
                }
                else
                {
                    options.Add(new Option(text, null));
                }
            }
            else
            {
                positionals.Add(args[i]);
            }
        }

        return new ParsedArgs(Name, options, positionals, rest);
    }
}

/// <summary>Parsed arguments of one command.</summary>
internal sealed class ParsedArgs(string command, List<Option> options, List<Arg> positionals, List<Arg>? rest)
{
    public string Command { get; } = command;

    /// <summary>Options in the order given (print's operations depend on it).</summary>
    public IReadOnlyList<Option> Options { get; } = options;

    public IReadOnlyList<Arg> Positionals { get; } = positionals;

    /// <summary>The arguments after <c>--</c>, or null if there was none.</summary>
    public IReadOnlyList<Arg>? Rest { get; } = rest;

    public bool Has(string option) => Options.Any(o => o.Name == option);

    public Arg? Get(string option)
    {
        foreach (var o in Options)
        {
            if (o.Name == option)
            {
                return o.Value;
            }
        }

        return null;
    }

    public string? GetText(string option) => Get(option)?.Text;

    /// <summary>All positionals, including those after <c>--</c>, for commands without a COMMAND tail.</summary>
    public IReadOnlyList<Arg> AllPositionals => Rest is null ? Positionals : [.. Positionals, .. Rest];

    public Arg Single(string what)
    {
        var all = AllPositionals;
        return all.Count == 1 ? all[0] : throw TestkitException.Usage($"{Command}: expected one {what}, got {all.Count} arguments");
    }

    public (Arg First, Arg Second) Pair(string first, string second)
    {
        var all = AllPositionals;
        return all.Count == 2
            ? (all[0], all[1])
            : throw TestkitException.Usage($"{Command}: expected {first} and {second}, got {all.Count} arguments");
    }

    public void NoPositionals()
    {
        if (AllPositionals.Count != 0)
        {
            throw TestkitException.Usage($"{Command}: unexpected argument '{AllPositionals[0].Text}'");
        }
    }
}
