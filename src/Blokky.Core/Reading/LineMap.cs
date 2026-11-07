namespace Blokky.Core.Reading;

/// <summary>
/// Maps a UTF-16 index into the decoded, BOM-less plan text to its SY-1 position. Lines end at LF, CRLF or a lone CR,
/// and nothing else: U+2028, U+2029 and NEL are not line breaks (YAML 1.2). Columns count code points, so a character
/// outside the BMP is one column.
/// </summary>
public sealed class LineMap
{
    private readonly int _length;
    private readonly int[] _lineStarts;

    // The UTF-16 index of the low half of every surrogate pair, ascending: each is a code unit that isn't a column.
    private readonly int[] _pairLows;

    /// <summary>Builds the map for <paramref name="text"/>.</summary>
    public LineMap(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        _length = text.Length;
        var starts = new List<int> { 0 };
        var pairLows = new List<int>();
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c == '\n')
            {
                starts.Add(i + 1);
            }
            else if (c == '\r')
            {
                if (i + 1 < text.Length && text[i + 1] == '\n')
                {
                    i++;
                }

                starts.Add(i + 1);
            }
            else if (char.IsHighSurrogate(c) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                pairLows.Add(++i);
            }
        }

        _lineStarts = [.. starts];
        _pairLows = [.. pairLows];
    }

    /// <summary>The number of lines; text ending in a line break has an empty last line.</summary>
    public int LineCount => _lineStarts.Length;

    /// <summary>
    /// The position of the UTF-16 index <paramref name="index"/>, clamped to the text (its end included). O(log n): the
    /// column is the distance from the line start, less the surrogate pairs completed in between.
    /// </summary>
    public PlanPosition At(int index)
    {
        index = Math.Clamp(index, 0, _length);
        var line = Array.BinarySearch(_lineStarts, index);
        if (line < 0)
        {
            line = ~line - 1;
        }

        var start = _lineStarts[line];
        var column = index - start + 1;
        if (_pairLows.Length > 0)
        {
            column -= LowerBound(_pairLows, index) - LowerBound(_pairLows, start);
        }

        return new PlanPosition(line + 1, column);
    }

    /// <summary>The UTF-16 index where the 1-based <paramref name="line"/> starts.</summary>
    public int LineStart(int line) => _lineStarts[Math.Clamp(line, 1, _lineStarts.Length) - 1];

    /// <summary>The number of values in <paramref name="sorted"/> below <paramref name="value"/>.</summary>
    private static int LowerBound(int[] sorted, int value)
    {
        var found = Array.BinarySearch(sorted, value);
        return found >= 0 ? found : ~found;
    }
}
