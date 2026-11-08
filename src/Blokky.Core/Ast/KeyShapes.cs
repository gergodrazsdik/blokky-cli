using System.Collections.Frozen;
using System.Text;

namespace Blokky.Core.Ast;

/// <summary>The kinds of fixed-shape mapping in a plan, each with its keys in the schema's <c>properties</c> order (SY-30).</summary>
public enum MappingShape
{
    /// <summary>The root mapping: the plan-only keys, then every node key.</summary>
    Root,

    /// <summary>A node.</summary>
    Node,

    /// <summary>An input declaration.</summary>
    Input,

    /// <summary><c>defaults:</c>.</summary>
    Defaults,

    /// <summary>A condition.</summary>
    Condition,

    /// <summary>A probe.</summary>
    Probe,

    /// <summary><c>for:</c>.</summary>
    For,

    /// <summary><c>for.items</c>.</summary>
    ForItems,

    /// <summary><c>for.lines</c>.</summary>
    ForLines,

    /// <summary><c>lock: { … }</c>.</summary>
    Lock,

    /// <summary><c>tempdir: { … }</c>.</summary>
    Tempdir,

    /// <summary><c>container:</c>.</summary>
    Container,

    /// <summary>A <c>container.mounts</c> entry.</summary>
    Mount,

    /// <summary>A <c>services</c> entry.</summary>
    Service,

    /// <summary><c>backoff:</c>.</summary>
    Backoff,

    /// <summary><c>index: { … }</c>.</summary>
    Index,

    /// <summary>The long form of a <c>carry</c> entry.</summary>
    Carry,

    /// <summary><c>{ var: X }</c>.</summary>
    VarReference,

    /// <summary><c>{ from_env: NAME }</c>.</summary>
    FromEnvReference,

    /// <summary><c>stdin: { file: P }</c>.</summary>
    StdinFile,
}

/// <summary>The keys of each <see cref="MappingShape"/> and the unknown-key suggestions (SY-30).</summary>
public static class KeyShapes
{
    /// <summary>The plan-only keys (SY-32), allowed only at the root (SY-33).</summary>
    public static readonly IReadOnlyList<string> PlanOnly = ["blokky", "inputs", "vars", "outputs", "defaults"];

    /// <summary>The kind keys (reference.md §2), in the schema's order.</summary>
    public static readonly IReadOnlyList<string> Kinds =
        ["exec", "script", "do", "all", "any", "race", "if", "sleep", "log", "fail", "assert", "set", "return"];

    /// <summary>Every key a node may carry, in the schema's order.</summary>
    public static readonly IReadOnlyList<string> NodeKeys =
    [
        .. Kinds,
        "shell", "then", "else", "level", "message", "code", "concurrency", "fail_fast",
        "stdin", "ok_exit", "stderr", "encoding", "keep_descendants", "capture", "capture_file", "secret",
        "name", "when", "timeout", "lock", "tempdir", "cwd", "env", "export", "container", "services", "finally", "finally_timeout",
        "ignore_errors",
        "retry", "while", "until", "for", "attempt_timeout", "max", "every", "backoff", "jitter", "on_body_failure", "index", "carry",
        "collect",
    ];

    /// <summary>Condition subjects, in the schema's order.</summary>
    public static readonly IReadOnlyList<string> Subjects = ["var", "env", "path", "command", "host", "result", "probe", "port", "output"];

    /// <summary>Condition operators, in the schema's order.</summary>
    public static readonly IReadOnlyList<string> Operators =
    [
        "equals", "not_equals", "in", "not_in", "contains", "not_contains", "matches", "not_matches", "empty", "not_empty",
        "defined", "not_defined", "exists", "not_exists", "is_file", "is_dir", "open", "gt", "ge", "lt", "le",
    ];

    private static readonly FrozenDictionary<MappingShape, string[]> Shapes = new Dictionary<MappingShape, string[]>
    {
        [MappingShape.Root] = [.. PlanOnly, .. NodeKeys],
        [MappingShape.Node] = [.. NodeKeys],
        [MappingShape.Input] = ["default", "description", "in", "matches"],
        [MappingShape.Defaults] = ["shell", "leaf_timeout"],
        [MappingShape.Condition] = [.. Subjects, .. Operators, "json", "ignore_case", "all", "any", "not"],
        [MappingShape.Probe] = ["exec", "script", "shell", "env", "export", "timeout", "true_exit", "false_exit", "timeout_result"],
        [MappingShape.For] =
        [
            "each", "count", "glob", "items", "lines", "matrix", "as", "concurrency", "fail_fast", "allow_empty", "max_items", "fields",
            "keep_blank",
        ],
        [MappingShape.ForItems] = ["var", "json"],
        [MappingShape.ForLines] = ["var"],
        [MappingShape.Lock] = ["name", "wait"],
        [MappingShape.Tempdir] = ["as", "prefix", "remove"],
        [MappingShape.Container] = ["image", "mounts", "env_pass"],
        [MappingShape.Mount] = ["host", "path", "read_only"],
        [MappingShape.Service] = ["name", "exec", "script", "shell", "env", "export", "cwd", "ready", "ready_timeout", "on_exit"],
        [MappingShape.Backoff] = ["factor", "max"],
        [MappingShape.Index] = ["as", "start"],
        [MappingShape.Carry] = ["from", "initial"],
        [MappingShape.VarReference] = ["var", "secret"],
        [MappingShape.FromEnvReference] = ["from_env", "secret"],
        [MappingShape.StdinFile] = ["file"],
    }.ToFrozenDictionary();

    private static readonly FrozenDictionary<MappingShape, FrozenSet<string>> Sets =
        Shapes.ToFrozenDictionary(p => p.Key, p => p.Value.ToFrozenSet(StringComparer.Ordinal));

    /// <summary>The synonym table of SY-30: (written, suggestion, message); the suggestion is the key it names.</summary>
    private static readonly (string Written, string Key, string Hint)[] NodeSynonyms =
    [
        ("register", "capture", "capture"),
        ("loop", "for", "for: { each: [...], as: ITEM }"),
        ("with_items", "for", "for: { each: [...], as: ITEM }"),
        ("retries", "retry", "retry: true plus max: N+1 (max counts attempts, including the first)"),
        ("steps", "do", "do"),
        ("tasks", "do", "do"),
        ("run", "script", "script (with shell) or exec"),
        ("continue-on-error", "ignore_errors", "ignore_errors"),
        ("exit", "return", "return (ends the enclosing sequence; there is no break)"),
        ("continue", "return", "return (ends the enclosing sequence; there is no break)"),
        ("break", "return", "return (ends the enclosing sequence; there is no break)"),
        ("timeout-minutes", "timeout", "timeout (with a unit, e.g. 30m)"),
        ("working-directory", "cwd", "cwd"),
        ("working_dir", "cwd", "cwd"),
        ("environment", "env", "env"),
    ];

    private static readonly (string Written, string Key, string Hint)[] InputSynonyms =
    [
        ("enum", "in", "in"),
        ("options", "in", "in"),
        ("choices", "in", "in"),
        ("pattern", "matches", "matches"),
        ("regex", "matches", "matches"),
        ("desc", "description", "description"),
        ("help", "description", "description"),
        ("value", "default", "default"),
    ];

    private static readonly (string Written, string Key, string Hint)[] RootSynonyms =
    [
        ("params", "inputs", "inputs"),
        ("parameters", "inputs", "inputs"),
        ("args", "inputs", "inputs"),
        ("arguments", "inputs", "inputs"),
        ("variables", "vars", "vars"),
        ("constants", "vars", "vars"),
    ];

    /// <summary>The keys of <paramref name="shape"/>, in the schema's order.</summary>
    public static IReadOnlyList<string> KeysOf(MappingShape shape) => Shapes[shape];

    /// <summary>Whether <paramref name="key"/> is a key of <paramref name="shape"/>.</summary>
    public static bool IsKey(MappingShape shape, string key) => Sets[shape].Contains(key);

    /// <summary>
    /// The suggestion for an unknown key (SY-30): a synonym first, then the closest valid key by edit distance after simple
    /// uppercase mapping, at most 2, ties to the first listed; null if none. The hint is the synonym's longer form.
    /// </summary>
    public static (string? Suggestion, string? Hint) Suggest(MappingShape shape, string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        IEnumerable<(string Written, string Key, string Hint)> synonyms = shape switch
        {
            MappingShape.Root => [.. RootSynonyms, .. NodeSynonyms],
            MappingShape.Node => NodeSynonyms,
            MappingShape.Input => InputSynonyms,
            _ => [],
        };
        foreach (var (written, suggestion, hint) in synonyms)
        {
            if (written == key)
            {
                return (suggestion, hint);
            }
        }

        string? best = null;
        var bestDistance = 3;
        var upper = Upper(key);
        foreach (var candidate in Shapes[shape])
        {
            // The difference in code points bounds the distance (UTF-16 lengths would count a key outside the BMP twice);
            // it also keeps a huge key cheap.
            var other = Upper(candidate);
            if (Math.Abs(other.Length - upper.Length) >= bestDistance)
            {
                continue;
            }

            var d = Distance(upper, other, bestDistance);
            if (d < bestDistance)
            {
                (best, bestDistance) = (candidate, d);
            }
        }

        return (best, null);
    }

    private static int[] Upper(string text)
    {
        var runes = new List<int>(text.Length);
        foreach (var rune in text.EnumerateRunes())
        {
            runes.Add(Rune.ToUpperInvariant(rune).Value);
        }

        return [.. runes];
    }

    /// <summary>Levenshtein distance over code points, or <paramref name="cap"/> once it can't be below it.</summary>
    private static int Distance(int[] a, int[] b, int cap)
    {
        var previous = new int[b.Length + 1];
        var current = new int[b.Length + 1];
        for (var j = 0; j <= b.Length; j++)
        {
            previous[j] = j;
        }

        for (var i = 1; i <= a.Length; i++)
        {
            current[0] = i;
            var rowMin = i;
            for (var j = 1; j <= b.Length; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                current[j] = Math.Min(Math.Min(previous[j] + 1, current[j - 1] + 1), previous[j - 1] + cost);
                rowMin = Math.Min(rowMin, current[j]);
            }

            if (rowMin >= cap)
            {
                return cap;
            }

            (previous, current) = (current, previous);
        }

        return previous[b.Length];
    }
}
