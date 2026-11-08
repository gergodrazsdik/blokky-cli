using System.Text;
using Blokky.Core.Ast;

namespace Blokky.Core.Syntax;

/// <summary>A segment of a JSON path: an object member (<see cref="Member"/>) or an array index.</summary>
/// <param name="Member">The member name, or null for an index.</param>
/// <param name="Index">The array index, when <paramref name="Member"/> is null.</param>
public sealed record JsonPathSegment(string? Member, int Index);

/// <summary>
/// The jq-like JSON path subset (syntax.md §6.8, SY-63 to SY-68): <c>.</c>, <c>.name</c>, <c>.["any key"]</c> and
/// <c>[N]</c>. Used by <c>json:</c> in conditions, <c>for.items.json</c>, field captures and <c>for.fields</c>.
/// </summary>
public static class JsonPath
{
    /// <summary>
    /// Parses <paramref name="text"/>; the segments are empty for <c>.</c>, the whole value. Null when valid, otherwise the
    /// rule of the first mistake: SY-65 (member names), SY-66 (brackets), SY-67 (indexes) or SY-68 (unsupported jq forms).
    /// </summary>
    public static GrammarError? Parse(string text, out IReadOnlyList<JsonPathSegment> segments)
    {
        ArgumentNullException.ThrowIfNull(text);
        var list = new List<JsonPathSegment>();
        segments = list;
        if (Unsupported(text) is { } unsupported)
        {
            return unsupported;
        }

        if (text.Length == 0 || text[0] != '.')
        {
            return text.StartsWith('[')
                ? new("SY-66", $"'{text}': a bracket path starts with a dot", "." + text)
                : new("SY-65", $"'{text}' isn't a JSON path: it starts with '.' (. is the whole value, .name a member)",
                    text.Length == 0 ? "." : "." + text);
        }

        if (text == ".")
        {
            return null;
        }

        var i = 0;
        while (i < text.Length)
        {
            if (text[i] == '.')
            {
                i++;
                if (i == text.Length)
                {
                    return new("SY-65", $"'{text}': a '.' must be followed by a member name or a [");
                }

                if (text[i] == '.')
                {
                    return new("SY-68", $"'{text}': recursive descent (..) isn't in the JSON path subset");
                }

                if (text[i] != '[')
                {
                    var start = i;
                    while (i < text.Length && (char.IsAsciiLetter(text[i]) || text[i] == '_' || (i > start && char.IsAsciiDigit(text[i]))))
                    {
                        i++;
                    }

                    if (i == start || (i < text.Length && text[i] is not ('.' or '[')))
                    {
                        return new("SY-65", $"'{text}': .name takes letters, digits and '_' (not starting with a digit); "
                            + "write any other key as .[\"key\"]");
                    }

                    list.Add(new(text[start..i], 0));
                    continue;
                }
            }
            else if (text[i] != '[')
            {
                return new("SY-65", $"'{text}': expected '.' or '[' at '{text[i..]}'");
            }

            if (Bracket(text, ref i, list) is { } error)
            {
                return error;
            }
        }

        return null;
    }

    /// <summary>The jq forms outside the subset that a pass over the text finds: pipes and filters (SY-68).</summary>
    private static GrammarError? Unsupported(string text)
    {
        var inString = false;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (inString)
            {
                if (c == '\\')
                {
                    i++;
                }
                else if (c == '"')
                {
                    inString = false;
                }

                continue;
            }

            switch (c)
            {
                case '"':
                    inString = true;
                    break;
                case '|':
                    return new("SY-68", $"'{text}': pipes aren't in the JSON path subset; capture the value and work on it in a script");
                case '(':
                    return new("SY-68", $"'{text}': filters and functions aren't in the JSON path subset");
            }
        }

        return null;
    }

    /// <summary>At a '[': <c>["key"]</c> or <c>[N]</c>.</summary>
    private static GrammarError? Bracket(string text, ref int i, List<JsonPathSegment> list)
    {
        i++;
        if (i == text.Length)
        {
            return new("SY-67", $"'{text}': a '[' is never closed");
        }

        var c = text[i];
        if (c == ']')
        {
            return new("SY-68", $"'{text}': wildcards ([]) aren't in the JSON path subset; use for: {{ items }} to go over an array");
        }

        if (c == '"')
        {
            if (JsonString(text, ref i, out var key) is { } stringError)
            {
                return stringError;
            }

            if (i == text.Length || text[i] != ']')
            {
                return new("SY-66", $"'{text}': a bracket key is one double-quoted string, then ']'");
            }

            i++;
            list.Add(new(key, 0));
            return null;
        }

        if (c == '\'')
        {
            return new("SY-66", $"'{text}': bracket keys are double-quoted (.[\"key\"])");
        }

        var start = i;
        if (c == '-')
        {
            i++;
        }

        while (i < text.Length && char.IsAsciiDigit(text[i]))
        {
            i++;
        }

        var digits = text[start..i];
        if (i < text.Length && text[i] == ':')
        {
            return new("SY-68", $"'{text}': slices aren't in the JSON path subset");
        }

        if (c == '-' && digits.Length > 1 && i < text.Length && text[i] == ']')
        {
            return new("SY-68", $"'{text}': negative indexes aren't in the JSON path subset");
        }

        if (c == '-' || digits.Length == 0 || i == text.Length || text[i] != ']')
        {
            return new("SY-67", $"'{text}': an index is a non-negative decimal integer ([0]); a key is double-quoted ([\"key\"])");
        }

        if (digits.Length > 1 && digits[0] == '0')
        {
            return new("SY-67", $"'{text}': an index has no leading zeros", null);
        }

        if (digits.Length > 10 || !int.TryParse(digits, System.Globalization.NumberStyles.None,
            System.Globalization.CultureInfo.InvariantCulture, out var index))
        {
            return new("SY-67", $"'{text}': the index {digits} is too large");
        }

        i++;
        list.Add(new(null, index));
        return null;
    }

    /// <summary>At a '"': a JSON string (RFC 8259 escapes), decoded.</summary>
    private static GrammarError? JsonString(string text, ref int i, out string value)
    {
        var sb = new StringBuilder();
        value = "";
        i++;
        while (i < text.Length)
        {
            var c = text[i];
            if (c == '"')
            {
                i++;
                value = sb.ToString();
                return null;
            }

            if (c < 0x20)
            {
                return new("SY-66", $"'{text}': a control character in a bracket key must be escaped");
            }

            if (c != '\\')
            {
                sb.Append(c);
                i++;
                continue;
            }

            if (i + 1 == text.Length)
            {
                break;
            }

            var e = text[i + 1];
            i += 2;
            switch (e)
            {
                case '"' or '\\' or '/':
                    sb.Append(e);
                    break;
                case 'b':
                    sb.Append('\b');
                    break;
                case 'f':
                    sb.Append('\f');
                    break;
                case 'n':
                    sb.Append('\n');
                    break;
                case 'r':
                    sb.Append('\r');
                    break;
                case 't':
                    sb.Append('\t');
                    break;
                case 'u' when i + 4 <= text.Length && !text.AsSpan(i, 4).ContainsAnyExcept(HexDigits):
                    sb.Append((char)Convert.ToInt32(text.Substring(i, 4), 16));
                    i += 4;
                    break;
                default:
                    return new("SY-66", $"'{text}': a bracket key uses JSON string escapes (\\\" \\\\ \\/ \\b \\f \\n \\r \\t \\uXXXX)");
            }
        }

        return new("SY-66", $"'{text}': a bracket key's double quote is never closed");
    }

    private static readonly System.Buffers.SearchValues<char> HexDigits =
        System.Buffers.SearchValues.Create("0123456789abcdefABCDEF");
}
