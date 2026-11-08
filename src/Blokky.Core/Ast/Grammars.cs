using System.Globalization;

namespace Blokky.Core.Ast;

/// <summary>Why a scalar doesn't match its grammar: the rule reported as <c>detail.rule</c> (SY-3) and a message.</summary>
/// <param name="Rule">The rule ID.</param>
/// <param name="Message">What is wrong.</param>
/// <param name="Fix">A corrected value, for targeted errors (SY-91).</param>
public sealed record GrammarError(string Rule, string Message, string? Fix = null);

/// <summary>The scalar grammars of syntax.md §6, on the text as written (SY-23 to SY-27). Each is linear in the text.</summary>
public static class Grammars
{
    /// <summary>The longest duration, 168h (SY-59).</summary>
    public static readonly TimeSpan MaxDuration = TimeSpan.FromHours(168);

    /// <summary>The spec versions this build supports (SY-35).</summary>
    public static IReadOnlyList<string> SupportedVersions { get; } = ["1.0"];

    /// <summary>SY-51: <c>MAJOR.MINOR</c>, no leading zeros.</summary>
    public static bool IsSpecVersion(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var dot = text.IndexOf('.', StringComparison.Ordinal);
        return dot > 0 && IsDecimalInteger(text.AsSpan(0, dot)) && IsDecimalInteger(text.AsSpan(dot + 1));
    }

    /// <summary>SY-52: a plan variable name, <c>^[A-Z][A-Z0-9_]*$</c>, not starting with <c>BLOKKY_</c>.</summary>
    public static GrammarError? VariableName(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (!IsUpperName(text))
        {
            return new("SY-52", $"'{text}' isn't a variable name: an uppercase letter, then uppercase letters, digits and '_'");
        }

        return text.StartsWith("BLOKKY_", StringComparison.Ordinal)
            ? new("SY-52", $"'{text}': the BLOKKY_ prefix is reserved")
            : null;
    }

    /// <summary>SY-52: an environment variable name, <c>^[A-Za-z_][A-Za-z0-9_]*$</c>.</summary>
    public static GrammarError? EnvironmentName(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var ok = text.Length > 0 && (char.IsAsciiLetter(text[0]) || text[0] == '_')
            && text.All(c => char.IsAsciiLetterOrDigit(c) || c == '_');
        return ok ? null : new("SY-52", $"'{text}' isn't an environment variable name: a letter or '_', then letters, digits and '_'");
    }

    /// <summary>SY-52, ER-1: a user error code, <c>^[A-Z][A-Z0-9_]*$</c>, not starting with <c>BK_</c>.</summary>
    public static GrammarError? UserCode(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (!IsUpperName(text))
        {
            return new("SY-52", $"'{text}' isn't an error code: an uppercase letter, then uppercase letters, digits and '_'");
        }

        return text.StartsWith("BK_", StringComparison.Ordinal)
            ? new("SY-52", $"'{text}': the BK_ prefix is reserved for built-in codes")
            : null;
    }

    /// <summary>SY-52: a lock or service name, <c>^[a-z0-9][a-z0-9._-]{0,63}$</c>.</summary>
    public static GrammarError? LowerName(string text, string what)
    {
        ArgumentNullException.ThrowIfNull(text);
        var ok = text.Length is >= 1 and <= 64 && IsLowerOrDigit(text[0])
            && text.All(c => IsLowerOrDigit(c) || c is '.' or '_' or '-');
        return ok ? null : new("SY-52", $"'{text}' isn't a {what}: a lowercase letter or digit, then up to 63 of a-z, 0-9, '.', '_', '-'");
    }

    /// <summary>
    /// reference.md §4.5: a tempdir prefix, <c>^[A-Za-z0-9._-]{1,32}$</c>. No rule ID names it; SY-88 is the per-field table.
    /// </summary>
    public static GrammarError? TempdirPrefix(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var ok = text.Length is >= 1 and <= 32 && text.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-');
        return ok ? null : new("SY-88", $"'{text}' isn't a tempdir prefix: 1 to 32 letters, digits, '.', '_' and '-'");
    }

    /// <summary>SY-53: exactly <c>true</c> or <c>false</c>, with a fix-it for the other spellings (SY-24).</summary>
    public static GrammarError? Boolean(string text, out bool value)
    {
        ArgumentNullException.ThrowIfNull(text);
        value = text == "true";
        if (text is "true" or "false")
        {
            return null;
        }

        var fix = text.ToUpperInvariant() switch
        {
            "TRUE" or "YES" or "Y" or "ON" or "1" => "true",
            "FALSE" or "NO" or "N" or "OFF" or "0" => "false",
            _ => null,
        };
        return new("SY-53", fix is null
            ? $"'{text}' isn't a boolean: write true or false"
            : $"'{text}' isn't a boolean: write {fix} (only true and false are booleans)", fix);
    }

    /// <summary>SY-53, CO-8: a field where only <c>true</c> is meaningful.</summary>
    public static GrammarError? TrueOnly(string text) =>
        text == "true" ? null : new("SY-53", $"'{text}': the only value here is true", "true");

    /// <summary>
    /// SY-54: a decimal integer, no sign, no leading zeros, at most 2 147 483 647, within [<paramref name="min"/>,
    /// <paramref name="max"/>]. Exit codes (SY-96) pass their own rule and range.
    /// </summary>
    public static GrammarError? WholeNumber(string text, long min, long max, out long value, string rule = "SY-54")
    {
        ArgumentNullException.ThrowIfNull(text);
        value = 0;
        var limit = rule == "SY-96" ? 4_294_967_295L : int.MaxValue;
        if (!IsDecimalInteger(text))
        {
            return new(rule, $"'{text}' isn't an integer: decimal digits, no sign, no leading zeros, no exponent");
        }

        if (text.Length > 10 || !long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value) || value > limit)
        {
            return new(rule, $"'{text}' is too large: at most {limit.ToString(CultureInfo.InvariantCulture)}");
        }

        return value < min || value > max
            ? new(rule, string.Create(CultureInfo.InvariantCulture, $"'{text}' is out of range: {min} to {max}"))
            : null;
    }

    /// <summary>SY-55: a decimal, <c>^(0|[1-9][0-9]*)(\.[0-9]+)?$</c>, within the field's range.</summary>
    public static GrammarError? DecimalNumber(string text, decimal min, decimal max, bool minExclusive, out decimal value)
    {
        ArgumentNullException.ThrowIfNull(text);
        value = 0;
        var dot = text.IndexOf('.', StringComparison.Ordinal);
        var whole = dot < 0 ? text.AsSpan() : text.AsSpan(0, dot);
        var ok = IsDecimalInteger(whole)
            && (dot < 0 || (dot < text.Length - 1 && !text.AsSpan(dot + 1).ContainsAnyExceptInRange('0', '9')));
        if (!ok || text.Length > 28 || !decimal.TryParse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out value))
        {
            return new("SY-55", $"'{text}' isn't a decimal: digits with an optional fraction (1.5), no sign or exponent");
        }

        var low = minExclusive ? value <= min : value < min;
        return low || value > max
            ? new("SY-55", string.Create(CultureInfo.InvariantCulture,
                $"'{text}' is out of range: {(minExclusive ? "above " : "")}{min} to {max}"))
            : null;
    }

    /// <summary>CO-19: a numeric operand, <c>^-?[0-9]+(\.[0-9]+)?$</c>.</summary>
    public static GrammarError? NumericOperand(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var s = text.StartsWith('-') ? text.AsSpan(1) : text.AsSpan();
        var dot = s.IndexOf('.');
        var whole = dot < 0 ? s : s[..dot];
        var ok = whole.Length > 0 && !whole.ContainsAnyExceptInRange('0', '9')
            && (dot < 0 || (dot < s.Length - 1 && !s[(dot + 1)..].ContainsAnyExceptInRange('0', '9')));
        return ok ? null : new("CO-19", $"'{text}' isn't a number: digits with an optional '-' and fraction (-1.5)");
    }

    /// <summary>
    /// SY-56 to SY-59: a duration of <c>&lt;integer&gt;&lt;unit&gt;</c> parts, units <c>h</c>, <c>m</c>, <c>s</c>, <c>ms</c>
    /// once each in that order; positive unless <paramref name="zeroAllowed"/> (then only <c>0s</c>); at most 168h.
    /// </summary>
    public static GrammarError? Duration(string text, bool zeroAllowed, out TimeSpan value)
    {
        ArgumentNullException.ThrowIfNull(text);
        value = TimeSpan.Zero;
        if (text.Length > 0 && !text.AsSpan().ContainsAnyExceptInRange('0', '9'))
        {
            return new("SY-57", $"'{text}' is a bare number: a duration needs a unit ({text}s, {text}m)", text + "s");
        }

        if (text.Contains('.', StringComparison.Ordinal) || text.Contains('+', StringComparison.Ordinal)
            || text.Contains('-', StringComparison.Ordinal))
        {
            return new("SY-57", $"'{text}': durations have no sign and no fractions (1500ms, not 1.5s)");
        }

        var lastUnit = -1;
        var leadingZero = false;
        var tooLarge = false;
        long ms = 0;
        var i = 0;
        while (i < text.Length)
        {
            var start = i;
            while (i < text.Length && char.IsAsciiDigit(text[i]))
            {
                i++;
            }

            var digits = text.AsSpan(start, i - start);
            int unit;
            long scale;
            if (i + 1 < text.Length && text[i] == 'm' && text[i + 1] == 's')
            {
                (unit, scale) = (3, 1);
                i += 2;
            }
            else if (i < text.Length && text[i] is 'h' or 'm' or 's')
            {
                (unit, scale) = text[i] switch { 'h' => (0, 3_600_000L), 'm' => (1, 60_000L), _ => (2, 1_000L) };
                i++;
            }
            else
            {
                return BadDuration(text);
            }

            if (digits.IsEmpty || unit <= lastUnit)
            {
                return BadDuration(text);
            }

            lastUnit = unit;
            leadingZero |= digits.Length > 1 && digits[0] == '0';
            if (digits.Length > 12 || !long.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var n))
            {
                tooLarge = true;
            }
            else
            {
                ms += n * scale;
                tooLarge |= ms > (long)MaxDuration.TotalMilliseconds;
            }
        }

        if (lastUnit < 0)
        {
            return BadDuration(text);
        }

        if (leadingZero)
        {
            return new("SY-57", $"'{text}': the numbers in a duration have no leading zeros");
        }

        if (tooLarge)
        {
            return new("SY-59", $"'{text}' is longer than the maximum duration, 168h");
        }

        value = TimeSpan.FromMilliseconds(ms);
        if (ms == 0 && !(zeroAllowed && text == "0s"))
        {
            return new("SY-58", zeroAllowed
                ? $"'{text}': write a zero duration as 0s"
                : $"'{text}': this duration must be positive");
        }

        return null;
    }

    private static GrammarError BadDuration(string text) =>
        new("SY-56", $"'{text}' isn't a duration: parts like 1h30m, 10s, 500ms, units h, m, s, ms in that order, no spaces");

    private static bool IsDecimalInteger(ReadOnlySpan<char> s) =>
        s.Length > 0 && !s.ContainsAnyExceptInRange('0', '9') && (s.Length == 1 || s[0] != '0');

    /// <summary>The variable-name grammar without the <c>BLOKKY_</c> check: <c>^[A-Z][A-Z0-9_]*$</c> (VA-4).</summary>
    internal static bool IsUpperName(string text) =>
        text.Length > 0 && char.IsAsciiLetterUpper(text[0])
        && text.All(c => char.IsAsciiLetterUpper(c) || char.IsAsciiDigit(c) || c == '_');

    private static bool IsLowerOrDigit(char c) => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c);
}
