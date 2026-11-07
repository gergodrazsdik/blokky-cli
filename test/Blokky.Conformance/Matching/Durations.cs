using System.Globalization;
using System.Text.RegularExpressions;

namespace Blokky.Conformance.Matching;

/// <summary>The plan's duration grammar plus <c>0s</c> (CF-37, syntax.md §6.5).</summary>
internal static partial class Durations
{
    public static bool TryParse(string? text, out TimeSpan duration)
    {
        duration = default;
        if (text is null)
        {
            return false;
        }

        var m = Grammar().Match(text);
        if (!m.Success)
        {
            return false;
        }

        long Part(string name) => m.Groups[name].Success ? long.Parse(m.Groups[name].Value, CultureInfo.InvariantCulture) : 0;
        duration = TimeSpan.FromHours(Part("h")) + TimeSpan.FromMinutes(Part("m")) + TimeSpan.FromSeconds(Part("s"))
            + TimeSpan.FromMilliseconds(Part("ms"));
        return true;
    }

    public static TimeSpan Parse(string text) =>
        TryParse(text, out var d) ? d : throw new FormatException($"'{text}' is not a duration");

    public static string Format(TimeSpan duration) =>
        string.Create(CultureInfo.InvariantCulture, $"{(long)duration.TotalMilliseconds}ms");

    private const string Pattern =
        "^(?=[0-9])(?:(?<h>0|[1-9][0-9]*)h)?(?:(?<m>0|[1-9][0-9]*)m(?!s))?(?:(?<s>0|[1-9][0-9]*)s)?(?:(?<ms>0|[1-9][0-9]*)ms)?$";

    [GeneratedRegex(Pattern)]
    private static partial Regex Grammar();
}
