namespace Blokky.Core.Reading;

/// <summary>
/// Maps a UTF-16 index into the decoded, BOM-less plan text to its SY-1 position. Lines end at LF, CRLF or a lone CR,
/// and nothing else: U+2028, U+2029 and NEL are not line breaks (YAML 1.2). Columns count code points, so a character
/// outside the BMP is one column.
/// </summary>
public sealed class LineMap
{
    private readonly string _text;
    private readonly int[] _lineStarts;

    /// <summary>Builds the map for <paramref name="text"/>.</summary>
    public LineMap(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        _text = text;
        var starts = new List<int> { 0 };
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '\n')
            {
                starts.Add(i + 1);
            }
            else if (text[i] == '\r')
            {
                if (i + 1 < text.Length && text[i + 1] == '\n')
                {
                    i++;
                }

                starts.Add(i + 1);
            }
        }

        _lineStarts = [.. starts];
    }

    /// <summary>The number of lines; text ending in a line break has an empty last line.</summary>
    public int LineCount => _lineStarts.Length;

    /// <summary>The position of the UTF-16 index <paramref name="index"/>, clamped to the text (its end included).</summary>
    public PlanPosition At(int index)
    {
        index = Math.Clamp(index, 0, _text.Length);
        var line = Array.BinarySearch(_lineStarts, index);
        if (line < 0)
        {
            line = ~line - 1;
        }

        var column = 1;
        for (var i = _lineStarts[line]; i < index; i++)
        {
            if (!char.IsLowSurrogate(_text[i]) || i == _lineStarts[line] || !char.IsHighSurrogate(_text[i - 1]))
            {
                column++;
            }
        }

        return new PlanPosition(line + 1, column);
    }

    /// <summary>The UTF-16 index where the 1-based <paramref name="line"/> starts.</summary>
    public int LineStart(int line) => _lineStarts[Math.Clamp(line, 1, _lineStarts.Length) - 1];
}
