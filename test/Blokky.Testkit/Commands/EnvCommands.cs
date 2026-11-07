using System.Collections;

namespace Blokky.Testkit.Commands;

/// <summary><c>env</c> and <c>getenv</c> (TK-13).</summary>
internal static class EnvCommands
{
    public static int Env(IReadOnlyList<Arg> args)
    {
        var parsed = Testkit.Prepare(new CommandSpec("env").Flag("--all"), args);
        var names = parsed.AllPositionals.Select(a => a.Text).ToList();
        if (parsed.Has("--all"))
        {
            if (names.Count > 0)
            {
                throw TestkitException.Usage("env: --all takes no names");
            }

            var all = new List<KeyValuePair<string, string>>();
            foreach (DictionaryEntry entry in Environment.GetEnvironmentVariables())
            {
                all.Add(new((string)entry.Key, (string?)entry.Value ?? ""));
            }

            all.Sort((a, b) => string.CompareOrdinal(a.Key, b.Key));
            StdStream.Out.Write(Json.Line(w =>
            {
                w.WriteStartObject();
                foreach (var (name, value) in all)
                {
                    w.WriteString(name, value);
                }

                w.WriteEndObject();
            }));
            return 0;
        }

        if (names.Count == 0)
        {
            throw TestkitException.Usage("env: expected NAME... or --all");
        }

        StdStream.Out.Write(Json.Line(w =>
        {
            w.WriteStartObject();
            foreach (var name in names)
            {
                // Looked up as the OS does: case-insensitively on Windows (the BCL does that there).
                w.WriteString(name, Environment.GetEnvironmentVariable(name));
            }

            w.WriteEndObject();
        }));
        return 0;
    }

    public static int GetEnv(IReadOnlyList<Arg> args)
    {
        var parsed = Testkit.Prepare(new CommandSpec("getenv"), args);
        var name = parsed.Single("NAME").Text;
        var value = Environment.GetEnvironmentVariable(name);
        if (value is null)
        {
            return 3;
        }

        StdStream.Out.Write(System.Text.Encoding.UTF8.GetBytes(value + "\n"));
        return 0;
    }
}
