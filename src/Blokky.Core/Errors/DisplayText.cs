using System.Globalization;
using System.Text;

namespace Blokky.Core.Errors;

/// <summary>Display sanitization for human-readable output (output-contract.md OC-4). Hygiene, not a control.</summary>
public static class DisplayText
{
    /// <summary>Escapes C0 and C1 control characters, ESC and DEL as <c>\xNN</c> or <c>\uNNNN</c>.</summary>
    public static string Escape(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (!text.Any(IsControl))
        {
            return text;
        }

        var sb = new StringBuilder(text.Length + 8);
        foreach (var c in text)
        {
            if (!IsControl(c))
            {
                sb.Append(c);
            }
            else if (c <= 0xFF)
            {
                sb.Append(CultureInfo.InvariantCulture, $"\\x{(int)c:x2}");
            }
            else
            {
                sb.Append(CultureInfo.InvariantCulture, $"\\u{(int)c:x4}");
            }
        }

        return sb.ToString();
    }

    private static bool IsControl(char c) => c < 0x20 || c is >= '\x7f' and <= '\x9f';
}
