using System.Globalization;

namespace Blokky.Testkit;

/// <summary>Value grammars of the interface: durations and sizes (TK-2), hex (TK-3), exit codes (TK-9).
/// Reimplemented from the spec, not shared with Blokky.Core (docs/design/testkit.md §1).</summary>
internal static class Grammar
{
    private static readonly TimeSpan MaxDuration = TimeSpan.FromHours(168);
    private static decimal? _timeScale;

    /// <summary>A plan duration (SY-56 to SY-59) or <c>0s</c>, multiplied by <c>TESTKIT_TIME_SCALE</c> (TK-2).</summary>
    public static TimeSpan Duration(string option, string text)
    {
        var unscaled = ParseDuration(text) ?? throw TestkitException.Usage($"{option}: not a duration: '{text}'");
        var scale = TimeScale();
        return scale == 1m ? unscaled : TimeSpan.FromTicks((long)decimal.Round(unscaled.Ticks * scale));
    }

    /// <summary>Parses SY-56 to SY-59 plus <c>0s</c>; null if malformed.</summary>
    public static TimeSpan? ParseDuration(string text)
    {
        if (text == "0s")
        {
            return TimeSpan.Zero;
        }

        var units = new[] { "h", "m", "s", "ms" };
        var lastUnit = -1;
        var i = 0;
        long totalMs = 0;
        if (text.Length == 0)
        {
            return null;
        }

        while (i < text.Length)
        {
            var start = i;
            while (i < text.Length && char.IsAsciiDigit(text[i]))
            {
                i++;
            }

            var digits = text[start..i];
            if (digits.Length == 0 || (digits.Length > 1 && digits[0] == '0') || digits.Length > 12)
            {
                return null;
            }

            int unit;
            if (i + 1 < text.Length && text[i] == 'm' && text[i + 1] == 's')
            {
                unit = 3;
                i += 2;
            }
            else if (i < text.Length && text[i] is 'h' or 'm' or 's')
            {
                unit = Array.IndexOf(units, text[i].ToString());
                i++;
            }
            else
            {
                return null;
            }

            if (unit <= lastUnit)
            {
                return null;
            }

            lastUnit = unit;
            var value = long.Parse(digits, NumberStyles.None, CultureInfo.InvariantCulture);
            totalMs += unit switch
            {
                0 => value * 3_600_000,
                1 => value * 60_000,
                2 => value * 1000,
                _ => value,
            };
            if (totalMs > MaxDuration.TotalMilliseconds)
            {
                return null;
            }
        }

        // Zero is written 0s only (SY-58).
        return totalMs == 0 ? null : TimeSpan.FromMilliseconds(totalMs);
    }

    private static decimal TimeScale()
    {
        if (_timeScale is { } cached)
        {
            return cached;
        }

        var raw = Environment.GetEnvironmentVariable("TESTKIT_TIME_SCALE");
        decimal scale = 1m;
        if (!string.IsNullOrEmpty(raw))
        {
            if (!raw.All(c => char.IsAsciiDigit(c) || c == '.')
                || !decimal.TryParse(raw, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out scale)
                || scale <= 0m || scale > 1m)
            {
                throw TestkitException.Usage($"TESTKIT_TIME_SCALE must be a decimal greater than 0 and at most 1: '{raw}'");
            }
        }

        _timeScale = scale;
        return scale;
    }

    /// <summary>A size: <c>&lt;integer&gt;&lt;unit&gt;</c> with B, KiB, MiB, GiB (SY-60), or a bare number of bytes (TK-2).</summary>
    public static long Size(string option, string text)
    {
        var end = 0;
        while (end < text.Length && char.IsAsciiDigit(text[end]))
        {
            end++;
        }

        var digits = text[..end];
        long multiplier = text[end..] switch
        {
            "" or "B" => 1,
            "KiB" => 1L << 10,
            "MiB" => 1L << 20,
            "GiB" => 1L << 30,
            _ => 0,
        };
        if (digits.Length == 0 || digits.Length > 12 || (digits.Length > 1 && digits[0] == '0') || multiplier == 0)
        {
            throw TestkitException.Usage($"{option}: not a size: '{text}'");
        }

        return long.Parse(digits, NumberStyles.None, CultureInfo.InvariantCulture) * multiplier;
    }

    /// <summary>Pairs of hex digits (TK-3).</summary>
    public static byte[] Hex(string option, string text)
    {
        if (text.Length % 2 != 0 || !text.All(char.IsAsciiHexDigit))
        {
            throw TestkitException.Usage($"{option}: not pairs of hex digits: '{text}'");
        }

        return Convert.FromHexString(text);
    }

    /// <summary>An exit code: 0–255 on Unix, 0–4294967295 on Windows (TK-9), as the int the process returns.</summary>
    public static int ExitCode(string option, string text)
    {
        var max = OperatingSystem.IsWindows() ? uint.MaxValue : 255u;
        if (text.Length == 0 || text.Length > 10 || !text.All(char.IsAsciiDigit)
            || !ulong.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var value) || value > max)
        {
            throw TestkitException.Usage($"{option}: not an exit code (0-{max}): '{text}'");
        }

        return unchecked((int)(uint)value);
    }

    /// <summary>A non-negative decimal integer with an upper bound.</summary>
    public static int Integer(string option, string text, int min, int max)
    {
        if (text.Length == 0 || text.Length > 9 || !text.All(char.IsAsciiDigit)
            || !int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var value) || value < min || value > max)
        {
            throw TestkitException.Usage($"{option}: not an integer from {min} to {max}: '{text}'");
        }

        return value;
    }

    /// <summary>The line ending of <c>--newline</c> (TK-10, TK-18).</summary>
    public static byte[] Newline(string? text) => text switch
    {
        null or "lf" => "\n"u8.ToArray(),
        "crlf" => "\r\n"u8.ToArray(),
        "none" => [],
        _ => throw TestkitException.Usage($"--newline: expected lf, crlf or none: '{text}'"),
    };
}
