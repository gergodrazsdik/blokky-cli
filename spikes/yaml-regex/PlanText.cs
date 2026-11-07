// Spike 3: encoding pre-pass and character lint (SY-5..SY-13), SY-1 positions.
using System.Buffers;
using System.Text;

record PlanError(string Code, int Line, int Col, string Detail = "");

/// SY-1 positions: lines end at LF, CRLF or a lone CR; columns are 1-based code points.
sealed class LineMap
{
    readonly string _t;
    readonly List<int> _starts = [0];

    public LineMap(string t)
    {
        _t = t;
        for (int i = 0; i < t.Length; i++)
        {
            if (t[i] == '\n') _starts.Add(i + 1);
            else if (t[i] == '\r')
            {
                if (i + 1 < t.Length && t[i + 1] == '\n') i++;
                _starts.Add(i + 1);
            }
        }
    }

    /// UTF-16 index into the BOM-less text -> (line, column).
    public (int Line, int Col) At(int index)
    {
        int lo = 0, hi = _starts.Count - 1;
        while (lo < hi)
        {
            int mid = (lo + hi + 1) / 2;
            if (_starts[mid] <= index) lo = mid; else hi = mid - 1;
        }
        int col = 1;
        for (int i = _starts[lo]; i < index && i < _t.Length; i++)
            if (!char.IsLowSurrogate(_t[i])) col++;
        return (lo + 1, col);
    }
}

static class PlanText
{
    public const int MaxBytes = 4 * 1024 * 1024;

    /// Returns errors (which end processing) or the decoded text without the BOM.
    public static List<PlanError> Decode(byte[] raw, out string text)
    {
        text = "";
        var errors = new List<PlanError>();
        if (raw.Length > MaxBytes)
        {
            errors.Add(new("BK_PLAN_TOO_LARGE", 1, 1));
            return errors;
        }
        ReadOnlySpan<byte> b = raw;
        // SY-6: UTF-32 before UTF-16 (FF FE 00 00 starts with FF FE).
        if (b.StartsWith((byte[])[0xFF, 0xFE, 0, 0]) || b.StartsWith((byte[])[0, 0, 0xFE, 0xFF])
            || b.StartsWith((byte[])[0xFF, 0xFE]) || b.StartsWith((byte[])[0xFE, 0xFF]))
        {
            errors.Add(new("BK_PLAN_ENCODING", 1, 1, "utf16/32 bom"));
            return errors;
        }
        int start = b.StartsWith((byte[])[0xEF, 0xBB, 0xBF]) ? 3 : 0;
        // SY-7: first invalid byte (overlong forms, surrogates, truncation all rejected by Rune).
        for (int i = start; i < b.Length;)
        {
            var st = Rune.DecodeFromUtf8(b[i..], out _, out int used);
            if (st != OperationStatus.Done)
            {
                string prefix = Encoding.UTF8.GetString(b[start..i]);
                var (l, c) = new LineMap(prefix).At(prefix.Length);
                errors.Add(new("BK_PLAN_ENCODING", l, c, $"offset={i}"));
                return errors;
            }
            i += used;
        }
        text = Encoding.UTF8.GetString(b[start..]);
        return errors;
    }

    /// SY-10..SY-13: one pass over every character, comments and keys included.
    public static List<PlanError> Lint(string t, LineMap map)
    {
        var errors = new List<PlanError>();
        for (int i = 0; i < t.Length;)
        {
            Rune.DecodeFromUtf16(t.AsSpan(i), out Rune r, out int used);
            int v = r.Value;
            bool bad =
                (v < 0x20 && v != 0x09 && v != 0x0A && !(v == 0x0D && i + 1 < t.Length && t[i + 1] == '\n'))
                || (v >= 0x7F && v <= 0x9F)
                || v == 0x061C || v == 0x200E || v == 0x200F || (v >= 0x202A && v <= 0x202E)
                || (v >= 0x2066 && v <= 0x2069)
                || (v >= 0x200B && v <= 0x200D) || v == 0x2060 || v == 0xFEFF;
            if (bad)
            {
                var (l, c) = map.At(i);
                errors.Add(new("BK_PLAN_CHARACTER", l, c, $"codepoint=U+{v:X4}"));
            }
            i += used;
        }
        return errors;
    }
}
