using System.Globalization;
using System.Text.RegularExpressions;
using Blokky.Conformance.Yaml;

namespace Blokky.Conformance.Matching;

/// <summary>A field, output or path id that isn't present in the result (CF-31).</summary>
internal sealed class Missing
{
    public static readonly Missing Value = new();

    private Missing()
    {
    }

    public override string ToString() => "(absent)";
}

/// <summary>An output marked secret, reported without a value (VA-47, <c>$secret</c>).</summary>
internal sealed class SecretValue
{
    public static readonly SecretValue Value = new();

    private SecretValue()
    {
    }

    public override string ToString() => "(secret)";
}

/// <summary>Timing tolerances (CF-42, CF-43).</summary>
/// <param name="MinSlack">Added to a measured duration before it is checked against <c>$min</c>.</param>
/// <param name="TimingFactor">Multiplies every duration <c>$max</c>.</param>
internal sealed record MatchOptions(TimeSpan MinSlack, double TimingFactor)
{
    public static MatchOptions Default { get; } = new(TimeSpan.FromMilliseconds(50), 1.0);
}

/// <summary>
/// Matches an expectation against a result value (conformance/README.md §3.7): mappings as subsets (CF-28), lists exactly
/// and in order (CF-29), scalars by value with integers equal to their decimal strings and durations compared as durations
/// (CF-30), <c>null</c> only against a present null (CF-31), and the <c>$</c> matchers. Returns the first mismatch as a
/// path into the expectation (CF-32), or null. A misused matcher throws <see cref="CaseFormatException"/> (a broken case).
/// </summary>
internal sealed partial class Matcher(MatchOptions options)
{
    private static readonly HashSet<string> Known =
        ["$absent", "$present", "$regex", "$one_of", "$min", "$max", "$unordered", "$contains", "$count", "$secret"];

    public Matcher()
        : this(MatchOptions.Default)
    {
    }

    public string? Match(object? expected, object? actual, string path) => Match(expected, actual, path, inDetail: false);

    /// <param name="inDetail">Whether <paramref name="actual"/> is an error's <c>detail</c> or lies below it.</param>
    private string? Match(object? expected, object? actual, string path, bool inDetail)
    {
        if (expected is OrderedDictionary<string, object?> map && IsMatcher(map))
        {
            return MatchMatcher(map, actual, path, inDetail);
        }

        switch (expected)
        {
            case OrderedDictionary<string, object?> subset:
                if (inDetail && actual is Missing && OnlyAbsent(subset))
                {
                    // CF-31: a field under a detail that isn't there isn't there either (detail: { suggestion: { $absent } }
                    // on an error without detail). Only within detail: elsewhere (a node, a result field) a missing
                    // mapping fails, so nodes: { /x: { error: { $absent: true } } } still needs node /x.
                    return null;
                }

                if (actual is not OrderedDictionary<string, object?> actualMap)
                {
                    return Mismatch(path, "a mapping", actual);
                }

                foreach (var (key, value) in subset)
                {
                    var found = actualMap.TryGetValue(key, out var a) ? a : Missing.Value;
                    if (Match(value, found, Join(path, key), inDetail || key == "detail") is { } mismatch)
                    {
                        return mismatch;
                    }
                }

                return null;

            case List<object?> list:
                if (actual is not List<object?> actualList)
                {
                    return Mismatch(path, "a list", actual);
                }

                if (actualList.Count != list.Count)
                {
                    return $"{Show(path)}: expected a list of {list.Count}, got {actualList.Count}: {YamlValues.Show(actualList)}";
                }

                for (var i = 0; i < list.Count; i++)
                {
                    if (Match(list[i], actualList[i], Index(path, i), inDetail) is { } mismatch)
                    {
                        return mismatch;
                    }
                }

                return null;

            case null:
                return actual is null ? null : Mismatch(path, "null", actual);

            default:
                return ScalarEquals(expected, actual) ? null : Mismatch(path, YamlValues.Show(expected), actual);
        }
    }

    /// <summary>Whether <paramref name="subset"/> has entries and every leaf of it is <c>{ $absent: true }</c>.</summary>
    private static bool OnlyAbsent(OrderedDictionary<string, object?> subset) =>
        subset.Count > 0 && subset.Values.All(v => v is OrderedDictionary<string, object?> m
            && (IsMatcher(m) ? m.Count == 1 && m.GetValueOrDefault("$absent") is true : OnlyAbsent(m)));

    internal static bool IsMatcher(OrderedDictionary<string, object?> map)
    {
        var dollar = map.Keys.Count(k => k.StartsWith('$'));
        if (dollar == 0)
        {
            return false;
        }

        foreach (var key in map.Keys.Where(k => k.StartsWith('$')))
        {
            if (!Known.Contains(key))
            {
                throw new CaseFormatException($"unknown matcher {key}");
            }
        }

        var isRange = map.Keys.All(k => k is "$min" or "$max");
        if (dollar != map.Count || (map.Count > 1 && !isRange))
        {
            throw new CaseFormatException(
                $"a matcher is a single-key mapping (only $min and $max pair up): {YamlValues.Show(map)}");
        }

        return true;
    }

    private string? MatchMatcher(OrderedDictionary<string, object?> matcher, object? actual, string path, bool inDetail)
    {
        var (key, arg) = (matcher.GetAt(0).Key, matcher.GetAt(0).Value);
        switch (key)
        {
            case "$absent":
                RequireTrue(key, arg);
                return actual is Missing ? null : Mismatch(path, "absent", actual);

            case "$present":
                RequireTrue(key, arg);
                return actual is Missing ? Mismatch(path, "present", actual) : null;

            case "$secret":
                RequireTrue(key, arg);
                return actual is SecretValue ? null : Mismatch(path, "a secret output", actual);

            case "$regex":
                {
                    if (arg is not string pattern)
                    {
                        throw new CaseFormatException("$regex takes a string pattern");
                    }

                    Regex regex;
                    try
                    {
                        regex = new Regex(pattern, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(5));
                    }
                    catch (ArgumentException ex)
                    {
                        throw new CaseFormatException($"$regex '{pattern}' doesn't compile: {ex.Message}");
                    }

                    var text = actual switch
                    {
                        string s => s,
                        long l => l.ToString(CultureInfo.InvariantCulture),
                        _ => null,
                    };
                    return text is not null && regex.IsMatch(text) ? null : Mismatch(path, $"a string matching /{pattern}/", actual);
                }

            case "$one_of":
                {
                    if (arg is not List<object?> options1 || options1.Count == 0)
                    {
                        throw new CaseFormatException("$one_of takes a non-empty list");
                    }

                    return options1.Any(o => Match(o, actual, path, inDetail) is null)
                        ? null
                        : Mismatch(path, "one of " + YamlValues.Show(options1), actual);
                }

            case "$min" or "$max":
                return MatchRange(matcher, actual, path);

            case "$count":
                {
                    if (actual is not List<object?> list)
                    {
                        return Mismatch(path, "a list", actual);
                    }

                    if (arg is not (long or OrderedDictionary<string, object?>))
                    {
                        throw new CaseFormatException("$count takes an integer or a $min/$max matcher");
                    }

                    return Match(arg, (long)list.Count, path + ".$count");
                }

            case "$unordered" or "$contains":
                {
                    if (arg is not List<object?> wanted)
                    {
                        throw new CaseFormatException($"{key} takes a list");
                    }

                    if (actual is not List<object?> list)
                    {
                        return Mismatch(path, "a list", actual);
                    }

                    if (key == "$unordered" && list.Count != wanted.Count)
                    {
                        return $"{Show(path)}: expected a list of {wanted.Count} in any order, got {list.Count}: {YamlValues.Show(list)}";
                    }

                    var unmatched = BipartiteMatch(wanted, list, path, inDetail);
                    return unmatched is null
                        ? null
                        : $"{Show(Index(path, unmatched.Value))}: no distinct element matches {YamlValues.Show(wanted[unmatched.Value])} "
                        + $"in {YamlValues.Show(list)}";
                }

            default:
                throw new CaseFormatException($"unknown matcher {key}");
        }
    }

    private string? MatchRange(OrderedDictionary<string, object?> matcher, object? actual, string path)
    {
        matcher.TryGetValue("$min", out var min);
        matcher.TryGetValue("$max", out var max);
        if (actual is string s && Durations.TryParse(s, out var parsed))
        {
            actual = parsed;
        }

        if (actual is TimeSpan measured)
        {
            if (min is not null && measured + options.MinSlack < AsDuration(min))
            {
                return Mismatch(path, $"at least {YamlValues.Show(min)}", actual);
            }

            if (max is not null && measured > AsDuration(max) * options.TimingFactor)
            {
                return Mismatch(path, $"at most {YamlValues.Show(max)} (timing factor {options.TimingFactor})", actual);
            }

            return null;
        }

        var number = AsNumber(actual);
        if (number is null)
        {
            return Mismatch(path, "a number or duration", actual);
        }

        if (min is not null && number < (AsNumber(min) ?? throw new CaseFormatException("$min takes a number or duration")))
        {
            return Mismatch(path, $"at least {YamlValues.Show(min)}", actual);
        }

        if (max is not null && number > (AsNumber(max) ?? throw new CaseFormatException("$max takes a number or duration")))
        {
            return Mismatch(path, $"at most {YamlValues.Show(max)}", actual);
        }

        return null;
    }

    /// <summary>Each expected element gets a distinct actual one (<see cref="Assign"/>). Returns an unmatched index.</summary>
    private int? BipartiteMatch(List<object?> wanted, List<object?> actual, string path, bool inDetail)
    {
        var fits = new bool[wanted.Count, actual.Count];
        for (var i = 0; i < wanted.Count; i++)
        {
            for (var j = 0; j < actual.Count; j++)
            {
                fits[i, j] = Match(wanted[i], actual[j], path, inDetail) is null;
            }
        }

        var matched = Assign(fits, Enumerable.Range(0, wanted.Count));
        var first = Array.IndexOf(matched, false);
        return first < 0 ? null : first;
    }

    /// <summary>
    /// A maximum matching by Kuhn's augmenting paths: whether each row of <paramref name="fits"/> gets a distinct column.
    /// Rows are tried in <paramref name="order"/>, and a row matched once stays matched, so earlier rows win the columns.
    /// </summary>
    internal static bool[] Assign(bool[,] fits, IEnumerable<int> order)
    {
        ArgumentNullException.ThrowIfNull(fits);
        ArgumentNullException.ThrowIfNull(order);
        var (rows, columns) = (fits.GetLength(0), fits.GetLength(1));
        var owner = Enumerable.Repeat(-1, columns).ToArray();
        var matched = new bool[rows];
        foreach (var i in order)
        {
            matched[i] = TryAssign(i, new bool[columns]);
        }

        return matched;

        bool TryAssign(int i, bool[] seen)
        {
            for (var j = 0; j < columns; j++)
            {
                if (!fits[i, j] || seen[j])
                {
                    continue;
                }

                seen[j] = true;
                if (owner[j] < 0 || TryAssign(owner[j], seen))
                {
                    owner[j] = i;
                    return true;
                }
            }

            return false;
        }
    }

    internal static bool ScalarEquals(object? expected, object? actual)
    {
        switch (expected, actual)
        {
            case (_, Missing or SecretValue or null):
                return false;
            case (string e, TimeSpan a):
                return Durations.TryParse(e, out var d) && d == a;
            case (string e, string a):
                return string.Equals(e, a, StringComparison.Ordinal);
            case (bool e, bool a):
                return e == a;
            case (bool, _) or (_, bool):
                return false;
        }

        var x = AsNumber(expected, strictText: true);
        var y = AsNumber(actual, strictText: true);
        return x is not null && y is not null && x == y;
    }

    /// <summary>
    /// A number, or a decimal integer string when <paramref name="strictText"/> (CF-30: <c>3</c> matches <c>'3'</c>);
    /// null when it isn't one or doesn't fit a decimal, so an out-of-range value never equals anything.
    /// </summary>
    private static decimal? AsNumber(object? value, bool strictText = false) => value switch
    {
        long l => l,
        int i => i,
        double d when Math.Abs(d) < DecimalLimit => (decimal)d,
        decimal m => m,
        string s when IntegerText().IsMatch(s) => decimal.TryParse(s, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture,
            out var m) ? m : null,
        string s when !strictText && decimal.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var m) => m,
        _ => null,
    };

    /// <summary>Doubles at or above this don't convert to decimal (the cast throws OverflowException); NaN fails the test.</summary>
    private const double DecimalLimit = 7.9e28;

    private static TimeSpan AsDuration(object? bound) => bound switch
    {
        string s when Durations.TryParse(s, out var d) => d,
        _ => throw new CaseFormatException($"a duration bound must be a duration, got {YamlValues.Show(bound)}"),
    };

    private static void RequireTrue(string key, object? arg)
    {
        if (arg is not true)
        {
            throw new CaseFormatException($"{key} takes true");
        }
    }

    private static string Mismatch(string path, string expected, object? actual) =>
        $"{Show(path)}: expected {expected}, got {(actual is Missing ? "(absent)" : YamlValues.Show(actual))}";

    private static string Show(string path) => path.Length == 0 ? "(root)" : path;

    internal static string Join(string path, string key) => path.Length == 0 ? key : $"{path}.{key}";

    private static string Index(string path, int i) => string.Create(CultureInfo.InvariantCulture, $"{path}[{i}]");

    [GeneratedRegex("^-?(0|[1-9][0-9]*)$")]
    private static partial Regex IntegerText();
}
