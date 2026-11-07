using System.Collections.Frozen;
using System.Globalization;
using System.Text;
using Blokky.Core.Errors;

namespace Blokky.Core.Reading;

/// <summary>
/// The character lint (syntax.md §2.4, SY-10 to SY-13): one pass over the decoded text, comments and keys included. Every
/// rejected character is an error with its position, code point and Unicode name; any error ends processing, so the
/// text isn't parsed as YAML.
/// </summary>
public static class CharacterLint
{
    /// <summary>Checks every character of <paramref name="text"/>; returns one error per rejected character.</summary>
    public static IReadOnlyList<BlokkyError> Check(string text, LineMap lines)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(lines);
        var errors = new List<BlokkyError>();
        for (var i = 0; i < text.Length;)
        {
            Rune.DecodeFromUtf16(text.AsSpan(i), out var rune, out var used);
            var crlf = rune.Value == '\r' && i + 1 < text.Length && text[i + 1] == '\n';
            if (IsRejected(rune.Value) && !crlf)
            {
                var codepoint = string.Create(CultureInfo.InvariantCulture, $"U+{rune.Value:X4}");
                errors.Add(ReaderErrors.Create(ErrorCodes.PlanCharacter, "SY-12", lines.At(i),
                    $"the plan contains {codepoint} {Name(rune.Value)}, a character that is rejected anywhere in a plan, "
                    + "comments included, because it can make the text read differently from what runs",
                    "codepoint", codepoint));
            }

            i += used;
        }

        return errors;
    }

    /// <summary>
    /// SY-11, SY-12: C0 controls except TAB and LF (CR is allowed only before LF, checked by the caller), DEL, C1 controls,
    /// bidirectional controls, zero-width characters, U+2028, U+2029 and U+FEFF (the leading BOM is gone by now).
    /// </summary>
    internal static bool IsRejected(int value) =>
        (value < 0x20 && value is not ('\t' or '\n'))
        || value is >= 0x7F and <= 0x9F
        || value is 0x061C or 0x200E or 0x200F or (>= 0x202A and <= 0x202E) or (>= 0x2066 and <= 0x2069)
        || value is (>= 0x200B and <= 0x200D) or 0x2060
        || value is 0x2028 or 0x2029 or 0xFEFF;

    /// <summary>The Unicode name (for controls, which have none, the name alias) of a rejected character.</summary>
    internal static string Name(int value) =>
        value switch
        {
            < 0x20 => C0Names[value],
            0x7F => "DELETE",
            >= 0x80 and <= 0x9F => C1Names[value - 0x80],
            _ => OtherNames.GetValueOrDefault(value, "(unnamed)"),
        };

    private static readonly string[] C0Names =
    [
        "NULL", "START OF HEADING", "START OF TEXT", "END OF TEXT", "END OF TRANSMISSION", "ENQUIRY", "ACKNOWLEDGE", "BELL",
        "BACKSPACE", "CHARACTER TABULATION", "LINE FEED", "LINE TABULATION", "FORM FEED", "CARRIAGE RETURN", "SHIFT OUT",
        "SHIFT IN", "DATA LINK ESCAPE", "DEVICE CONTROL ONE", "DEVICE CONTROL TWO", "DEVICE CONTROL THREE",
        "DEVICE CONTROL FOUR", "NEGATIVE ACKNOWLEDGE", "SYNCHRONOUS IDLE", "END OF TRANSMISSION BLOCK", "CANCEL",
        "END OF MEDIUM", "SUBSTITUTE", "ESCAPE", "INFORMATION SEPARATOR FOUR", "INFORMATION SEPARATOR THREE",
        "INFORMATION SEPARATOR TWO", "INFORMATION SEPARATOR ONE",
    ];

    private static readonly string[] C1Names =
    [
        "PADDING CHARACTER", "HIGH OCTET PRESET", "BREAK PERMITTED HERE", "NO BREAK HERE", "INDEX", "NEXT LINE",
        "START OF SELECTED AREA", "END OF SELECTED AREA", "CHARACTER TABULATION SET", "CHARACTER TABULATION WITH JUSTIFICATION",
        "LINE TABULATION SET", "PARTIAL LINE FORWARD", "PARTIAL LINE BACKWARD", "REVERSE LINE FEED", "SINGLE SHIFT TWO",
        "SINGLE SHIFT THREE", "DEVICE CONTROL STRING", "PRIVATE USE ONE", "PRIVATE USE TWO", "SET TRANSMIT STATE",
        "CANCEL CHARACTER", "MESSAGE WAITING", "START OF GUARDED AREA", "END OF GUARDED AREA", "START OF STRING",
        "SINGLE GRAPHIC CHARACTER INTRODUCER", "SINGLE CHARACTER INTRODUCER", "CONTROL SEQUENCE INTRODUCER", "STRING TERMINATOR",
        "OPERATING SYSTEM COMMAND", "PRIVACY MESSAGE", "APPLICATION PROGRAM COMMAND",
    ];

    private static readonly FrozenDictionary<int, string> OtherNames = new Dictionary<int, string>
    {
        [0x061C] = "ARABIC LETTER MARK",
        [0x200B] = "ZERO WIDTH SPACE",
        [0x200C] = "ZERO WIDTH NON-JOINER",
        [0x200D] = "ZERO WIDTH JOINER",
        [0x200E] = "LEFT-TO-RIGHT MARK",
        [0x200F] = "RIGHT-TO-LEFT MARK",
        [0x2028] = "LINE SEPARATOR",
        [0x2029] = "PARAGRAPH SEPARATOR",
        [0x202A] = "LEFT-TO-RIGHT EMBEDDING",
        [0x202B] = "RIGHT-TO-LEFT EMBEDDING",
        [0x202C] = "POP DIRECTIONAL FORMATTING",
        [0x202D] = "LEFT-TO-RIGHT OVERRIDE",
        [0x202E] = "RIGHT-TO-LEFT OVERRIDE",
        [0x2060] = "WORD JOINER",
        [0x2066] = "LEFT-TO-RIGHT ISOLATE",
        [0x2067] = "RIGHT-TO-LEFT ISOLATE",
        [0x2068] = "FIRST STRONG ISOLATE",
        [0x2069] = "POP DIRECTIONAL ISOLATE",
        [0xFEFF] = "ZERO WIDTH NO-BREAK SPACE",
    }.ToFrozenDictionary();
}
