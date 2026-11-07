using System.Collections.Frozen;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Blokky.Core.Errors;

namespace Blokky.Core.Reading;

/// <summary>
/// The character lint (syntax.md §2.4, SY-10 to SY-13): one pass over the decoded text, comments and keys included. Every
/// rejected character is an error with its position, code point and Unicode name; any error ends processing, so the
/// text isn't parsed as YAML.
/// </summary>
/// <remarks>
/// SY-13 reports each rejected character, and nothing bounds their number (output-contract.md OC-12 lists every error),
/// so a text full of them gives an error per character. The pass is linear: it tracks the line and column itself, and
/// each error shares its message and a parsed <c>detail</c> with the other errors for the same character, so an error
/// costs little more than its record and its <c>pos</c>.
/// </remarks>
public static class CharacterLint
{
    /// <summary>Checks every character of <paramref name="text"/>; returns one error per rejected character.</summary>
    public static IReadOnlyList<BlokkyError> Check(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var errors = new List<BlokkyError>();
        Dictionary<int, Rejected>? seen = null;
        var line = 1;
        var column = 1;
        for (var i = 0; i < text.Length;)
        {
            Rune.DecodeFromUtf16(text.AsSpan(i), out var rune, out var used);
            var value = rune.Value;
            var crlf = value == '\r' && i + 1 < text.Length && text[i + 1] == '\n';
            if (IsRejected(value) && !crlf)
            {
                seen ??= [];
                if (!seen.TryGetValue(value, out var rejected))
                {
                    rejected = new Rejected(value);
                    seen.Add(value, rejected);
                }

                errors.Add(new BlokkyError
                {
                    Code = ErrorCodes.PlanCharacter,
                    Message = rejected.Message,
                    Pos = BlokkyError.FormatPos(line, column),
                    Detail = JsonObject.Create(rejected.Detail),
                });
            }

            i += used;
            if (value == '\n' || (value == '\r' && !crlf))
            {
                // SY-1: lines end at LF, CRLF (its CR is the LF's) or a lone CR.
                line++;
                column = 1;
            }
            else if (!crlf)
            {
                column++;
            }
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

    /// <summary>What every error for one rejected character shares: its message and its <c>detail</c>, parsed once.</summary>
    private sealed class Rejected
    {
        public Rejected(int value)
        {
            var codepoint = string.Create(CultureInfo.InvariantCulture, $"U+{value:X4}");
            Message = $"the plan contains {codepoint} {Name(value)}, a character that is rejected anywhere in a plan, "
                + "comments included, because it can make the text read differently from what runs";
            var detail = new JsonObject { ["rule"] = "SY-12", ["codepoint"] = codepoint };
            using var document = JsonDocument.Parse(detail.ToJsonString());
            Detail = document.RootElement.Clone();
        }

        public string Message { get; }

        /// <summary>
        /// <c>{"rule":"SY-12","codepoint":…}</c>. <see cref="JsonObject.Create(JsonElement, JsonNodeOptions?)"/> wraps it
        /// without copying until the object is read or changed, and a serializer writes the element as it is.
        /// </summary>
        public JsonElement Detail { get; }
    }

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
