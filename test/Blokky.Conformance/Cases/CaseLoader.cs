using System.Globalization;
using System.Text;
using Blokky.Conformance.Matching;
using Blokky.Conformance.Yaml;
using Blokky.Core.Features;

namespace Blokky.Conformance.Cases;

/// <summary>Reads case files (conformance/README.md §2, §3) and applies the platform overrides (§4).</summary>
internal static class CaseLoader
{
    /// <summary>The closed key list (CF-9).</summary>
    internal static readonly HashSet<string> Keys =
    [
        "title", "rules", "spec", "features", "requires", "clock", "plan", "plan_base64", "inputs", "env", "files",
        "programs", "actions", "after_run", "expect", "platforms", "notes",
    ];

    private static readonly HashSet<string> OverrideKeys = ["env", "files", "programs", "requires", "expect"];
    private static readonly HashSet<string> ValidationKeys = ["plan_errors", "input_errors", "exhaustive"];
    private static readonly HashSet<string> RunKeys = ["status", "error", "outputs", "inputs", "id", "nodes", "invocations", "files",
        "duration"];
    private static readonly HashSet<string> NoResultKeys = ["no_result", "files", "invocations"];

    /// <summary>Every <c>*.yaml</c> under <paramref name="casesDir"/>, ordered by case id.</summary>
    public static IReadOnlyList<string> FindCaseFiles(string casesDir) =>
        [.. Directory.EnumerateFiles(casesDir, "*.yaml", SearchOption.AllDirectories)
            .OrderBy(p => IdOf(casesDir, p), StringComparer.Ordinal)];

    /// <summary>The case id: the path below <c>cases/</c> without the extension, with <c>/</c> (CF-7).</summary>
    public static string IdOf(string casesDir, string path)
    {
        var relative = Path.GetRelativePath(casesDir, path).Replace(Path.DirectorySeparatorChar, '/');
        return relative.EndsWith(".yaml", StringComparison.Ordinal) ? relative[..^5] : relative;
    }

    public static ConformanceCase Load(string casesDir, string path, string os) =>
        Parse(IdOf(casesDir, path), File.ReadAllText(path, Encoding.UTF8), os);

    /// <summary>Parses a case for <paramref name="os"/> (<c>linux</c>, <c>macos</c> or <c>windows</c>).</summary>
    /// <exception cref="CaseFormatException">The case is broken (CF-62 <c>error</c>).</exception>
    public static ConformanceCase Parse(string id, string text, string os)
    {
        if (YamlValues.Load(text) is not OrderedDictionary<string, object?> raw)
        {
            throw new CaseFormatException("a case file is a mapping");
        }

        if (raw.Keys.FirstOrDefault(k => !Keys.Contains(k)) is { } unknown)
        {
            throw new CaseFormatException($"unknown top-level key '{unknown}' (CF-9)");
        }

        foreach (var required in new[] { "title", "features", "expect" })
        {
            if (!raw.ContainsKey(required))
            {
                throw new CaseFormatException($"missing required key '{required}'");
            }
        }

        if (!raw.ContainsKey("rules") && !raw.ContainsKey("spec"))
        {
            throw new CaseFormatException("a case cites rules or spec sections (§7)");
        }

        if (raw.ContainsKey("plan") == raw.ContainsKey("plan_base64"))
        {
            throw new CaseFormatException("a case has exactly one of plan and plan_base64");
        }

        CheckMatchers(raw["expect"], "expect", allowDelete: false);
        var (merged, skip) = ApplyOverrides(raw, os);

        var features = Strings(merged, "features");
        if (features.Count == 0)
        {
            throw new CaseFormatException("features lists at least one feature");
        }

        if (features.FirstOrDefault(f => !FeatureIds.IsKnown(f)) is { } badFeature)
        {
            throw new CaseFormatException($"unknown feature id '{badFeature}' (§3.2)");
        }

        var actions = ReadActions(merged);
        var expect = Map(merged["expect"], "expect");
        var kind = KindOf(expect, "expect");
        if (kind == ExpectKind.NoResult && !actions.Any(a => a.Kill))
        {
            throw new CaseFormatException("expect: { no_result: true } is only for a case with a kill action (CF-65)");
        }

        return new ConformanceCase
        {
            Id = id,
            Title = merged["title"] as string ?? throw new CaseFormatException("title is a string"),
            Rules = Strings(merged, "rules"),
            Spec = Strings(merged, "spec"),
            Features = features,
            Requires = ReadRequirements(merged.GetValueOrDefault("requires")),
            Clock = merged.ContainsKey("clock") ? Strings(merged, "clock") : null,
            Plan = PlanBytes(merged),
            Inputs = Strings(merged, "inputs"),
            Env = ReadEnv(merged.GetValueOrDefault("env")),
            Files = ReadFiles(merged.GetValueOrDefault("files")),
            Programs = ReadPrograms(merged.GetValueOrDefault("programs")),
            Actions = actions,
            AfterRun = ReadAfterRun(merged.GetValueOrDefault("after_run")),
            Expect = expect,
            ExpectKind = kind,
            PlatformSkip = skip,
        };
    }

    /// <summary>
    /// Merges the <c>unix</c> override, then the OS's own (CF-34): mappings key by key, recursively; lists and scalars
    /// replace; <c>{ $delete: true }</c> removes a key. Two <c>$min</c>/<c>$max</c> ranges merge like mappings; any other
    /// matcher on either side replaces the value, since merging two matchers would make an invalid one.
    /// </summary>
    internal static (OrderedDictionary<string, object?> Case, string? Skip) ApplyOverrides(
        OrderedDictionary<string, object?> raw, string os)
    {
        if (!raw.TryGetValue("platforms", out var platformsValue) || platformsValue is null)
        {
            return (raw, null);
        }

        var platforms = Map(platformsValue, "platforms");
        var keys = os == "windows" ? new[] { "windows" } : ["unix", os];
        if (platforms.Keys.FirstOrDefault(k => k is not ("unix" or "linux" or "macos" or "windows")) is { } badKey)
        {
            throw new CaseFormatException($"unknown platform key '{badKey}' (CF-33)");
        }

        var merged = Copy(raw);
        string? skip = null;
        foreach (var key in keys)
        {
            if (!platforms.TryGetValue(key, out var overrideValue))
            {
                continue;
            }

            var over = Map(overrideValue, $"platforms.{key}");
            if (over.TryGetValue("skip", out var reason))
            {
                if (over.Count != 1 || reason is not string text)
                {
                    throw new CaseFormatException($"platforms.{key}: skip takes a reason and stands alone (CF-33)");
                }

                skip = text;
                continue;
            }

            foreach (var (field, value) in over)
            {
                if (!OverrideKeys.Contains(field))
                {
                    throw new CaseFormatException($"platforms.{key}: '{field}' can't be overridden (CF-33, CF-34)");
                }

                CheckMatchers(value, $"platforms.{key}.{field}", allowDelete: true);
                MergeInto(merged, field, value);
            }
        }

        return (merged, skip);
    }

    internal static void MergeInto(OrderedDictionary<string, object?> target, string key, object? value)
    {
        if (value is OrderedDictionary<string, object?> { Count: 1 } del && del.TryGetValue("$delete", out var flag))
        {
            if (flag is not true)
            {
                throw new CaseFormatException("$delete takes true");
            }

            target.Remove(key);
            return;
        }

        if (value is OrderedDictionary<string, object?> overMap
            && target.TryGetValue(key, out var existing) && existing is OrderedDictionary<string, object?> baseMap
            && ((!IsMatcherMap(overMap) && !IsMatcherMap(baseMap)) || (IsRange(overMap) && IsRange(baseMap))))
        {
            var copy = Copy(baseMap);
            foreach (var (k, v) in overMap)
            {
                MergeInto(copy, k, v);
            }

            target[key] = copy;
            return;
        }

        target[key] = StripDeletes(value);
    }

    private static object? StripDeletes(object? value)
    {
        if (value is not OrderedDictionary<string, object?> map)
        {
            return value;
        }

        var copy = new OrderedDictionary<string, object?>(StringComparer.Ordinal);
        foreach (var (k, v) in map)
        {
            if (v is OrderedDictionary<string, object?> { Count: 1 } del && del.ContainsKey("$delete"))
            {
                continue;
            }

            copy[k] = StripDeletes(v);
        }

        return copy;
    }

    private static bool IsRange(OrderedDictionary<string, object?> map) => map.Count > 0 && map.Keys.All(k => k is "$min" or "$max");

    private static bool IsMatcherMap(OrderedDictionary<string, object?> map) =>
        map.Keys.Any(k => k.StartsWith('$') && k != "$delete");

    private static OrderedDictionary<string, object?> Copy(OrderedDictionary<string, object?> map) =>
        new(map, StringComparer.Ordinal);

    /// <summary>Checks every matcher statically, so a broken matcher is an error even in a case that is skipped.</summary>
    internal static void CheckMatchers(object? value, string path, bool allowDelete)
    {
        switch (value)
        {
            case OrderedDictionary<string, object?> map:
                if (allowDelete && map.Count == 1 && map.ContainsKey("$delete"))
                {
                    return;
                }

                try
                {
                    Matcher.IsMatcher(map);
                }
                catch (CaseFormatException ex)
                {
                    throw new CaseFormatException($"{path}: {ex.Message}");
                }

                foreach (var (k, v) in map)
                {
                    CheckMatchers(v, $"{path}.{k}", allowDelete);
                }

                break;
            case List<object?> list:
                for (var i = 0; i < list.Count; i++)
                {
                    CheckMatchers(list[i], string.Create(CultureInfo.InvariantCulture, $"{path}[{i}]"), allowDelete);
                }

                break;
        }
    }

    internal static ExpectKind KindOf(OrderedDictionary<string, object?> expect, string where)
    {
        if (expect.ContainsKey("no_result"))
        {
            if (expect["no_result"] is not true)
            {
                throw new CaseFormatException($"{where}.no_result is true");
            }

            CheckKeys(expect, NoResultKeys, where, "a no-result outcome");
            return ExpectKind.NoResult;
        }

        if (expect.ContainsKey("plan_errors") || expect.ContainsKey("input_errors"))
        {
            CheckKeys(expect, ValidationKeys, where, "a validation outcome");
            return ExpectKind.Validation;
        }

        if (expect.ContainsKey("status"))
        {
            CheckKeys(expect, RunKeys, where, "a run outcome");
            return ExpectKind.Run;
        }

        throw new CaseFormatException($"{where} holds a validation outcome, a run outcome or no_result (CF-25)");
    }

    private static void CheckKeys(OrderedDictionary<string, object?> map, HashSet<string> allowed, string where, string what)
    {
        if (map.Keys.FirstOrDefault(k => !allowed.Contains(k)) is { } bad)
        {
            throw new CaseFormatException($"{where}: '{bad}' doesn't belong in {what} (CF-25)");
        }
    }

    private static byte[] PlanBytes(OrderedDictionary<string, object?> c)
    {
        if (c.TryGetValue("plan", out var plan))
        {
            return plan is string text && text.Length > 0
                ? Encoding.UTF8.GetBytes(text)
                : throw new CaseFormatException("plan is a non-empty string");
        }

        try
        {
            return Convert.FromBase64String(c["plan_base64"] as string ?? throw new CaseFormatException("plan_base64 is a string"));
        }
        catch (FormatException)
        {
            throw new CaseFormatException("plan_base64 isn't valid base64");
        }
    }

    private static Requirements ReadRequirements(object? value)
    {
        if (value is null)
        {
            return Requirements.None;
        }

        var map = Map(value, "requires");
        if (map.Keys.FirstOrDefault(k => k is not ("os" or "shells" or "ports" or "container-engine")) is { } bad)
        {
            throw new CaseFormatException($"requires: unknown key '{bad}'");
        }

        var ports = map.TryGetValue("ports", out var p) && p is not null
            ? List(p, "requires.ports").Select(x => x is long n && n is >= 47000 and <= 47999
                ? (int)n
                : throw new CaseFormatException("requires.ports are integers in 47000-47999 (CF-14)")).ToList()
            : [];
        var engine = map.TryGetValue("container-engine", out var e) && e is not null
            ? e is true ? true : throw new CaseFormatException("requires.container-engine is true")
            : false;
        return new Requirements(Strings(map, "os"), Strings(map, "shells"), ports, engine);
    }

    private static Dictionary<string, string?> ReadEnv(object? value)
    {
        var env = new Dictionary<string, string?>(StringComparer.Ordinal);
        if (value is null)
        {
            return env;
        }

        foreach (var (name, v) in Map(value, "env"))
        {
            env[name] = v switch
            {
                null => null,
                string s => s,
                _ => throw new CaseFormatException($"env.{name} is a string or null (CF-19)"),
            };
        }

        return env;
    }

    private static Dictionary<string, CaseFile> ReadFiles(object? value)
    {
        var files = new Dictionary<string, CaseFile>(StringComparer.Ordinal);
        if (value is null)
        {
            return files;
        }

        foreach (var (path, v) in Map(value, "files"))
        {
            if (path.Length == 0 || path.StartsWith('/') || path.Contains('\\', StringComparison.Ordinal)
                || path.Split('/').Any(s => s is ".." or ""))
            {
                throw new CaseFormatException($"files: '{path}' isn't a relative path inside the work directory (CF-20)");
            }

            files[path] = v switch
            {
                string s => new CaseFile(Encoding.UTF8.GetBytes(s), false),
                OrderedDictionary<string, object?> m when m.ContainsKey("dir") =>
                    m.Count == 1 && m["dir"] is true ? new CaseFile(null, false)
                        : throw new CaseFormatException($"files.{path}: {{ dir: true }} stands alone"),
                OrderedDictionary<string, object?> m => ReadFileEntry(path, m),
                _ => throw new CaseFormatException($"files.{path} is a string or a mapping (CF-20)"),
            };
        }

        return files;
    }

    private static CaseFile ReadFileEntry(string path, OrderedDictionary<string, object?> m)
    {
        if (m.Keys.FirstOrDefault(k => k is not ("text" or "base64" or "executable")) is { } bad)
        {
            throw new CaseFormatException($"files.{path}: unknown key '{bad}'");
        }

        var executable = m.TryGetValue("executable", out var x) && (x is true ? true
            : throw new CaseFormatException($"files.{path}.executable is true"));
        if (m.TryGetValue("text", out var text) == m.ContainsKey("base64"))
        {
            throw new CaseFormatException($"files.{path} has exactly one of text and base64");
        }

        try
        {
            return text is string t
                ? new CaseFile(Encoding.UTF8.GetBytes(t), executable)
                : new CaseFile(Convert.FromBase64String((string)m["base64"]!), executable);
        }
        catch (Exception ex) when (ex is FormatException or InvalidCastException)
        {
            throw new CaseFormatException($"files.{path}: invalid content");
        }
    }

    private static Dictionary<string, IReadOnlyList<string>> ReadPrograms(object? value)
    {
        var programs = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        if (value is null)
        {
            return programs;
        }

        foreach (var (name, v) in Map(value, "programs"))
        {
            if (name.Length == 0 || name == "blokky-testkit" || !name.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-'))
            {
                throw new CaseFormatException($"programs: '{name}' isn't a valid alias name (CF-21)");
            }

            programs[name] = [.. List(v, $"programs.{name}").Select(a => a as string
                ?? throw new CaseFormatException($"programs.{name} lists strings"))];
        }

        return programs;
    }

    private static List<CaseAction> ReadActions(OrderedDictionary<string, object?> c)
    {
        if (!c.TryGetValue("actions", out var value) || value is null)
        {
            return [];
        }

        return [.. List(value, "actions").Select(item =>
        {
            var a = Map(item, "actions[]");
            if (a.Keys.FirstOrDefault(k => k is not ("after" or "stop" or "kill")) is { } bad)
            {
                throw new CaseFormatException($"actions: unknown key '{bad}'");
            }

            var after = a.GetValueOrDefault("after") as string;
            if (!Durations.TryParse(after, out var at))
            {
                throw new CaseFormatException("actions: after is a duration (CF-22)");
            }

            var stop = a.GetValueOrDefault("stop") is true;
            var kill = a.GetValueOrDefault("kill") is true;
            return stop == kill ? throw new CaseFormatException("actions: an entry has exactly one of stop: true and kill: true")
                : new CaseAction(at, kill);
        })];
    }

    private static List<AfterRunCheck> ReadAfterRun(object? value)
    {
        if (value is null)
        {
            return [];
        }

        return [.. List(value, "after_run").Select(item =>
        {
            var m = Map(item, "after_run[]");
            if (m.ContainsKey("argv"))
            {
                if (m.Count != 2 || !m.TryGetValue("exit_code", out var exitCode))
                {
                    throw new CaseFormatException("after_run: a testkit check has argv and exit_code (CF-64)");
                }

                return new AfterRunCheck(Strings(m, "argv"), exitCode, null, [], null, ExpectKind.Run);
            }

            if (m.Keys.FirstOrDefault(k => k is not ("plan" or "inputs" or "expect")) is { } bad)
            {
                throw new CaseFormatException($"after_run: unknown key '{bad}'");
            }

            var plan = m.GetValueOrDefault("plan") as string
                ?? throw new CaseFormatException("after_run: a plan check has plan and expect");
            CheckMatchers(m.GetValueOrDefault("expect"), "after_run.expect", allowDelete: false);
            var expect = Map(m.GetValueOrDefault("expect"), "after_run.expect");
            var kind = KindOf(expect, "after_run.expect");
            return kind == ExpectKind.NoResult
                ? throw new CaseFormatException("after_run: a second plan can't expect no_result")
                : new AfterRunCheck(null, null, Encoding.UTF8.GetBytes(plan), Strings(m, "inputs"), expect, kind);
        })];
    }

    private static List<string> Strings(OrderedDictionary<string, object?> map, string key) =>
        !map.TryGetValue(key, out var value) || value is null
            ? []
            : [.. List(value, key).Select(v => v as string ?? throw new CaseFormatException($"{key} lists strings"))];

    private static List<object?> List(object? value, string where) =>
        value as List<object?> ?? throw new CaseFormatException($"{where} is a list");

    private static OrderedDictionary<string, object?> Map(object? value, string where) =>
        value as OrderedDictionary<string, object?> ?? throw new CaseFormatException($"{where} is a mapping");
}
