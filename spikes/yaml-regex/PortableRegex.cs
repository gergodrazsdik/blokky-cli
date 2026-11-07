// Spike 9: the portable regex subset (SY-61, SY-62), validated and translated to
// .NET RegexOptions.NonBacktracking. Matching runs over the value's UTF-8 bytes,
// each byte >= 0x80 mapped to U+E000+byte (a private-use char, never a .NET word
// char). That makes `.` one code point, \b/\B ASCII-only, and (?i) independent of
// .NET's casing: classes are expanded with CO-12's simple uppercase mapping here.
using System.Text;
using System.Text.RegularExpressions;

sealed class RegexSubsetException(string m) : Exception(m);

/// A set of code points as sorted, disjoint, inclusive ranges.
sealed class CpSet
{
    public readonly List<(int Lo, int Hi)> R = [];
    public static CpSet Of(params (int, int)[] rs) { var s = new CpSet(); foreach (var r in rs) s.Add(r.Item1, r.Item2); return s; }
    public void Add(int lo, int hi) { R.Add((lo, hi)); Normalize(); }
    public void AddSet(CpSet o) { R.AddRange(o.R); Normalize(); }
    void Normalize()
    {
        R.Sort();
        var o = new List<(int, int)>();
        foreach (var (lo, hi) in R)
            if (o.Count > 0 && lo <= o[^1].Item2 + 1) o[^1] = (o[^1].Item1, Math.Max(o[^1].Item2, hi));
            else o.Add((lo, hi));
        R.Clear(); R.AddRange(o);
    }
    public bool Contains(int c) { foreach (var (lo, hi) in R) if (c >= lo && c <= hi) return true; return false; }
    public CpSet Intersect(CpSet o)
    {
        var s = new CpSet();
        foreach (var (lo, hi) in R)
            foreach (var (olo, ohi) in o.R)
            {
                int a = Math.Max(lo, olo), b = Math.Min(hi, ohi);
                if (a <= b) s.R.Add((a, b));
            }
        s.AddSet(new CpSet());
        return s;
    }
    public CpSet Complement()
    {
        var s = new CpSet(); int next = 0;
        foreach (var (lo, hi) in R) { if (lo > next) s.R.Add((next, lo - 1)); next = hi + 1; }
        if (next <= 0x10FFFF) s.R.Add((next, 0x10FFFF));
        return s;
    }
}

static class SimpleUpper
{
    public static readonly Dictionary<int, int> Up = [];
    static SimpleUpper() { for (int i = 0; i < CaseTable.Pairs.Length; i += 2) Up[CaseTable.Pairs[i]] = CaseTable.Pairs[i + 1]; }
    public static int Of(int c) => Up.TryGetValue(c, out var u) ? u : c;

    static CpSet? _domain;
    static CpSet Domain { get { if (_domain == null) { var d = new CpSet(); foreach (var k in Up.Keys) d.R.Add((k, k)); d.AddSet(new CpSet()); _domain = d; } return _domain; } }

    /// CO-12: {c | Up(c) in Up(S)} = (U minus domain) + {k in domain | Up(k) in U},
    /// where U = Up(S) = (S minus domain) + {Up(k) | k in S and domain}.
    public static CpSet Expand(CpSet s)
    {
        var u = s.Intersect(Domain.Complement());
        foreach (var (k, v) in Up) if (s.Contains(k)) u.Add(v, v);
        var result = u.Intersect(Domain.Complement());
        foreach (var (k, v) in Up) if (u.Contains(v)) result.Add(k, k);
        return result;
    }
}

sealed class PortableRegex
{
    readonly int[] _p;
    int _i;
    bool _ci;
    readonly StringBuilder _out = new();
    public Regex? Compiled;
    public string Translated = "";

    static readonly CpSet Digit = CpSet.Of((0x30, 0x39));
    static readonly CpSet Word = CpSet.Of((0x30, 0x39), (0x41, 0x5A), (0x5F, 0x5F), (0x61, 0x7A));
    static readonly CpSet Space = CpSet.Of((0x09, 0x0D), (0x20, 0x20)); // TAB LF VT FF CR, space
    static readonly CpSet Any = CpSet.Of((0, 0x10FFFF));
    const string Escapable = ".^$*+?()[]{}|\\/-";

    PortableRegex(string pattern, bool ignoreCase)
    {
        var cps = new List<int>();
        foreach (var r in pattern.EnumerateRunes()) cps.Add(r.Value);
        _p = [.. cps];
        _ci = ignoreCase;
    }

    /// Validates and translates; throws RegexSubsetException for RF-22.
    public static PortableRegex Compile(string pattern, bool ignoreCase = false)
    {
        var px = new PortableRegex(pattern, ignoreCase);
        if (pattern.StartsWith("(?i)", StringComparison.Ordinal)) { px._ci = true; px._i = 4; }
        string body = px.ParseAlt();
        if (px._i < px._p.Length) throw new RegexSubsetException($"unexpected '{(char)px._p[px._i]}'");
        // Unanchored search (CO-13) that can only start at a code point boundary.
        px.Translated = @"\A(?:" + Emit(Any) + ")*?(?:" + body + ")";
        try
        {
            px.Compiled = new Regex(px.Translated, RegexOptions.NonBacktracking | RegexOptions.CultureInvariant);
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException)
        {
            throw new RegexSubsetException("engine: " + e.Message.Split('\n')[0]);
        }
        return px;
    }

    public bool IsMatch(string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        var chars = new char[bytes.Length];
        for (int i = 0; i < bytes.Length; i++) chars[i] = bytes[i] < 0x80 ? (char)bytes[i] : (char)(0xE000 + bytes[i]);
        return Compiled!.IsMatch(chars);
    }

    bool More => _i < _p.Length;
    int Peek => _p[_i];

    string ParseAlt()
    {
        var parts = new List<string> { ParseConcat() };
        while (More && Peek == '|') { _i++; parts.Add(ParseConcat()); }
        return parts.Count == 1 ? parts[0] : "(?:" + string.Join("|", parts) + ")";
    }

    string ParseConcat()
    {
        var sb = new StringBuilder();
        while (More && Peek != '|' && Peek != ')')
        {
            var (atom, quantifiable) = ParseAtom();
            if (More && Peek is '*' or '+' or '?' or '{')
            {
                if (!quantifiable) throw new RegexSubsetException("quantifier without a repeatable atom");
                string q = ParseQuantifier();
                if (More && Peek is '*' or '+' or '?' or '{')
                    throw new RegexSubsetException("repeated quantifier (possessive or nested)");
                sb.Append("(?:").Append(atom).Append(')').Append(q);
            }
            else sb.Append(atom);
        }
        return sb.ToString();
    }

    string ParseQuantifier()
    {
        int c = _p[_i++];
        string q;
        if (c == '{')
        {
            int n = ParseInt(), m = n;
            if (More && Peek == ',')
            {
                _i++;
                m = More && Peek == '}' ? -1 : ParseInt();
            }
            if (!More || Peek != '}') throw new RegexSubsetException("bad counted repetition");
            _i++;
            if (n > 1000 || m > 1000 || (m >= 0 && m < n)) throw new RegexSubsetException("bad repetition bounds");
            q = m == n ? $"{{{n}}}" : m < 0 ? $"{{{n},}}" : $"{{{n},{m}}}";
        }
        else q = ((char)c).ToString();
        if (More && Peek == '?') { _i++; q += "?"; }
        return q;
    }

    int ParseInt()
    {
        int start = _i, v = 0;
        while (More && Peek is >= '0' and <= '9') { v = Math.Min(v * 10 + (Peek - '0'), 100000); _i++; }
        if (_i == start) throw new RegexSubsetException("expected a number");
        return v;
    }

    (string Atom, bool Quantifiable) ParseAtom()
    {
        int c = _p[_i++];
        switch (c)
        {
            case '(':
                if (More && Peek == '?')
                {
                    if (_i + 1 < _p.Length && _p[_i + 1] == ':') _i += 2;
                    else throw new RegexSubsetException("only (?:…) and a leading (?i) are in the subset");
                }
                string inner = ParseAlt();
                if (!More || Peek != ')') throw new RegexSubsetException("missing )");
                _i++;
                return ("(?:" + inner + ")", true);
            case ')': throw new RegexSubsetException("unbalanced )");
            case '[': return (Emit(ParseClass()), true);
            case '.': return (Emit(CpSet.Of((0, 9), (11, 0x10FFFF))), true);
            case '^': return (@"\A", false);
            case '$': return (@"\z", false);
            case '*' or '+' or '?' or '{': throw new RegexSubsetException("quantifier without an atom");
            case ']' or '}': throw new RegexSubsetException($"unescaped '{(char)c}'");
            case '\\':
            {
                if (!More) throw new RegexSubsetException("trailing backslash");
                int e = _p[_i];
                if (e == 'b') { _i++; return (@"\b", false); }
                if (e == 'B') { _i++; return (@"\B", false); }
                return (Emit(Lit(ParseEscape(inClass: false))), true);
            }
            default: return (Emit(Lit(CpSet.Of((c, c)))), true);
        }
    }

    CpSet Lit(CpSet s) => _ci ? SimpleUpper.Expand(s) : s;

    /// After a backslash: a class escape (\d …) or a single code point.
    CpSet ParseEscape(bool inClass)
    {
        int e = _p[_i++];
        switch (e)
        {
            case 'd': return Digit;
            case 'D': return Digit.Complement();
            case 'w': return Word;
            case 'W': return Word.Complement();
            case 's': return Space;
            case 'S': return Space.Complement();
            case 't': return CpSet.Of((9, 9));
            case 'n': return CpSet.Of((10, 10));
            case 'r': return CpSet.Of((13, 13));
            case 'x':
            {
                int v = Hex(2, 2);
                return CpSet.Of((v, v));
            }
            case 'u':
            {
                if (!More || Peek != '{') throw new RegexSubsetException(@"\u needs {…}");
                _i++;
                int v = Hex(1, 6);
                if (!More || Peek != '}') throw new RegexSubsetException(@"\u{ not closed");
                _i++;
                if (v > 0x10FFFF || (v >= 0xD800 && v <= 0xDFFF)) throw new RegexSubsetException("not a code point");
                return CpSet.Of((v, v));
            }
            default:
                if (e < 128 && Escapable.Contains((char)e)) return CpSet.Of((e, e));
                throw new RegexSubsetException($"escape \\{char.ConvertFromUtf32(e)} is outside the subset{(inClass ? " (in a class)" : "")}");
        }
    }

    int Hex(int min, int max)
    {
        int v = 0, n = 0;
        while (n < max && More && Uri.IsHexDigit((char)Math.Min(Peek, 0xFFFF)) && Peek < 128) { v = v * 16 + Convert.ToInt32(((char)Peek).ToString(), 16); _i++; n++; }
        if (n < min) throw new RegexSubsetException("bad hex escape");
        return v;
    }

    CpSet ParseClass()
    {
        bool neg = false;
        if (More && Peek == '^') { neg = true; _i++; }
        var set = new CpSet();
        bool first = true;
        while (true)
        {
            if (!More) throw new RegexSubsetException("missing ]");
            if (Peek == ']')
            {
                if (first) throw new RegexSubsetException("empty class");
                _i++;
                break;
            }
            var (lo, single) = ClassAtom(first);
            first = false;
            if (More && Peek == '-' && _i + 1 < _p.Length && _p[_i + 1] != ']')
            {
                _i++;
                var (hi, single2) = ClassAtom(false);
                if (single < 0 || single2 < 0) throw new RegexSubsetException("range with a class escape");
                if (single2 < single) throw new RegexSubsetException("range out of order");
                set.Add(single, single2);
            }
            else set.AddSet(lo);
        }
        // CO-12: fold before negating, so (?i)[^a] excludes a and A.
        if (_ci) set = SimpleUpper.Expand(set);
        return neg ? set.Complement() : set;
    }

    /// Returns the atom's set and, when it is one code point, that code point (else -1).
    (CpSet, int) ClassAtom(bool first)
    {
        int c = _p[_i++];
        if (c == '\\')
        {
            if (!More) throw new RegexSubsetException("trailing backslash");
            int e = Peek;
            if (e is 'b' or 'B') throw new RegexSubsetException(@"\b in a class");
            var s = ParseEscape(inClass: true);
            bool one = s.R.Count == 1 && s.R[0].Lo == s.R[0].Hi && e is not ('d' or 'w' or 's');
            return (s, one ? s.R[0].Lo : -1);
        }
        if (c == '[') throw new RegexSubsetException("unescaped [ in a class");
        if (c == '-' && !first && !(More && Peek == ']')) throw new RegexSubsetException("unescaped - in a class");
        return (CpSet.Of((c, c)), c);
    }

    // ---- emission: a code point set as a .NET pattern over the byte-mapped value ----

    static string B(int b) => b < 0x80 ? $"\\u{b:X4}" : $"\\u{0xE000 + b:X4}";

    static string Emit(CpSet s)
    {
        var alts = new List<string>();
        var ascii = new StringBuilder();
        // Fast path: every non-ASCII code point. The value is valid UTF-8, so the lead
        // byte alone fixes the length; exact counts keep matches on code point boundaries.
        bool allNonAscii = s.Intersect(CpSet.Of((0x80, 0xD7FF), (0xE000, 0x10FFFF))).R is [(0x80, 0xD7FF), (0xE000, 0x10FFFF)];
        if (allNonAscii)
        {
            alts.Add("(?:[\\uE0C0-\\uE0DF][\\uE080-\\uE0BF]|[\\uE0E0-\\uE0EF][\\uE080-\\uE0BF]{2}|[\\uE0F0-\\uE0F7][\\uE080-\\uE0BF]{3})");
            s = s.Intersect(CpSet.Of((0, 0x7F)));
        }
        foreach (var (lo0, hi0) in s.R)
        {
            if (lo0 < 0x80) ascii.Append(B(lo0)).Append('-').Append(B(Math.Min(hi0, 0x7F)));
            int lo = Math.Max(lo0, 0x80);
            foreach (var (a, b) in SplitBySurrogates(lo, hi0))
                foreach (var (x, y) in SplitByLength(a, b))
                    Utf8Seqs(x, y, alts);
        }
        if (ascii.Length > 0) alts.Insert(0, "[" + ascii + "]");
        if (alts.Count == 0) return "[^\\u0000-\\uFFFF]"; // matches nothing
        return alts.Count == 1 ? alts[0] : "(?:" + string.Join("|", alts) + ")";
    }

    static IEnumerable<(int, int)> SplitBySurrogates(int lo, int hi)
    {
        if (lo > hi) yield break;
        if (hi < 0xD800 || lo > 0xDFFF) { yield return (lo, hi); yield break; }
        if (lo < 0xD800) yield return (lo, 0xD7FF);
        if (hi > 0xDFFF) yield return (0xE000, hi);
    }

    static IEnumerable<(int, int)> SplitByLength(int lo, int hi)
    {
        foreach (int max in (int[])[0x7F, 0x7FF, 0xFFFF, 0x10FFFF])
        {
            if (lo > hi) yield break;
            if (lo <= max) { yield return (lo, Math.Min(hi, max)); lo = max + 1; }
        }
    }

    // Go regexp/syntax style UTF-8 range compilation; lo and hi encode to the same length.
    static void Utf8Seqs(int lo, int hi, List<string> alts)
    {
        int n = Encoding.UTF8.GetByteCount(char.ConvertFromUtf32(lo));
        for (int i = 1; i < n; i++)
        {
            int m = (1 << (6 * i)) - 1;
            if ((lo & ~m) != (hi & ~m))
            {
                if ((lo & m) != 0) { Utf8Seqs(lo, lo | m, alts); Utf8Seqs((lo | m) + 1, hi, alts); return; }
                if ((hi & m) != m) { Utf8Seqs(lo, (hi & ~m) - 1, alts); Utf8Seqs(hi & ~m, hi, alts); return; }
            }
        }
        var a = Encoding.UTF8.GetBytes(char.ConvertFromUtf32(lo));
        var b = Encoding.UTF8.GetBytes(char.ConvertFromUtf32(hi));
        var sb = new StringBuilder();
        for (int k = 0; k < a.Length; k++)
            sb.Append(a[k] == b[k] ? B(a[k]) : $"[{B(a[k])}-{B(b[k])}]");
        alts.Add(sb.ToString());
    }
}
