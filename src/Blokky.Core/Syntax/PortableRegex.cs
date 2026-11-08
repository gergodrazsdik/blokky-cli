using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Blokky.Core.Ast;

namespace Blokky.Core.Syntax;

/// <summary>
/// The portable regex subset (syntax.md §6.7: SY-61, SY-62, SY-104; RF-22), validated by its own parser and translated to
/// .NET's <see cref="RegexOptions.NonBacktracking"/> engine (spike 9). .NET's own <c>$</c>, <c>.</c>, <c>\d\w\s</c>,
/// <c>\b</c> and <c>IgnoreCase</c> differ from the subset, so the translation uses none of them: it matches over a
/// byte-mapped value (the value's UTF-8 bytes, each byte ≥ 0x80 mapped to U+E000 + byte, a character .NET never counts
/// as a word character), with every set compiled to UTF-8 byte sequences, <c>^</c>/<c>$</c> as <c>\A</c>/<c>\z</c>, and
/// <c>(?i)</c> expanded with CO-12's simple uppercase mapping. The search is unanchored (CO-13) and only starts on code
/// point boundaries.
/// </summary>
public sealed class PortableRegex
{
    /// <summary>The largest repetition count (SY-61).</summary>
    public const int MaxCount = 1000;

    /// <summary>The most nodes a pattern may have once counted repetitions are expanded (SY-104).</summary>
    public const int MaxNodes = 10_000;

    /// <summary>
    /// The NonBacktracking automaton limit. .NET's default (10 000) refuses patterns within SY-104 once their sets are
    /// UTF-8 byte sequences (a <c>.</c> costs about 50 nodes), so it is raised for the whole process before any
    /// NonBacktracking regex is built.
    /// </summary>
    private const int AutomataSize = 2_000_000;

    private readonly Regex _regex;

    static PortableRegex() => AppContext.SetData("REGEX_NONBACKTRACKING_MAX_AUTOMATA_SIZE", AutomataSize);

    private PortableRegex(string pattern, bool ignoreCase, string translated)
    {
        Pattern = pattern;
        IgnoreCase = ignoreCase;
        Translated = translated;
        _regex = new Regex(translated, RegexOptions.NonBacktracking | RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture);
    }

    /// <summary>The pattern as written.</summary>
    public string Pattern { get; }

    /// <summary>Whether the whole pattern ignores case: a leading <c>(?i)</c> or <c>ignore_case: true</c> (CO-12).</summary>
    public bool IgnoreCase { get; }

    /// <summary>The .NET pattern, over the byte-mapped value.</summary>
    public string Translated { get; }

    /// <summary>
    /// Checks <paramref name="pattern"/> against the subset and its size bound without translating it: linear in the
    /// pattern. Null when it is within; otherwise RF-22 with what is outside.
    /// </summary>
    public static GrammarError? Check(string pattern)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        return new Parser(pattern, emit: false, ignoreCase: false).Run(out _, out _);
    }

    /// <summary>
    /// Validates and translates <paramref name="pattern"/>; <paramref name="ignoreCase"/> applies <c>(?i)</c> to the whole
    /// pattern (<c>ignore_case: true</c>, CO-12). Null with <paramref name="error"/> (RF-22) when outside the subset.
    /// </summary>
    public static PortableRegex? Compile(string pattern, bool ignoreCase, out GrammarError? error)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        error = new Parser(pattern, emit: true, ignoreCase).Run(out var body, out var folded);
        if (error is not null)
        {
            return null;
        }

        var translated = @"\A(?:" + Emit(CodePointSet.All) + ")*?(?:" + body + ")";
        try
        {
            return new PortableRegex(pattern, folded, translated);
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException)
        {
            // The subset and SY-104 are meant to make this impossible; an engine refusal is still RF-22 (reference.md).
            error = new("RF-22", $"the regex '{pattern}' doesn't compile: {e.Message.Split('\n')[0]}");
            return null;
        }
    }

    /// <summary>Whether the pattern matches anywhere in <paramref name="value"/> (CO-13).</summary>
    public bool IsMatch(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var bytes = Encoding.UTF8.GetBytes(value);
        var chars = new char[bytes.Length];
        for (var i = 0; i < bytes.Length; i++)
        {
            chars[i] = bytes[i] < 0x80 ? (char)bytes[i] : (char)(0xE000 + bytes[i]);
        }

        return _regex.IsMatch(chars);
    }

    // ---- emission: a code point set as a .NET pattern over the byte-mapped value --------------------------------------

    private static readonly CodePointSet Ascii = CodePointSet.FromRanges([(0, 0x7F)]);
    private static readonly CodePointSet NonAscii = CodePointSet.FromRanges([(0x80, 0xD7FF), (0xE000, CodePointSet.MaxCodePoint)]);

    private static string B(int b) =>
        string.Create(CultureInfo.InvariantCulture, $"\\u{(b < 0x80 ? b : 0xE000 + b):X4}");

    private static string Emit(CodePointSet set)
    {
        var alts = new List<string>();

        // Every non-ASCII code point: the value is valid UTF-8, so the lead byte alone fixes the length, and exact counts keep
        // matches on code point boundaries.
        var nonAscii = set.Intersect(NonAscii);
        if (nonAscii.Ranges.SequenceEqual(NonAscii.Ranges))
        {
            alts.Add(@"(?:[\uE0C0-\uE0DF][\uE080-\uE0BF]|[\uE0E0-\uE0EF][\uE080-\uE0BF]{2}|[\uE0F0-\uE0F7][\uE080-\uE0BF]{3})");
        }
        else
        {
            foreach (var (lo, hi) in nonAscii.Ranges)
            {
                foreach (var (a, b) in SplitByLength(lo, hi))
                {
                    Utf8Sequences(a, b, alts);
                }
            }
        }

        var ascii = new StringBuilder();
        foreach (var (lo, hi) in set.Intersect(Ascii).Ranges)
        {
            ascii.Append(B(lo)).Append('-').Append(B(hi));
        }

        if (ascii.Length > 0)
        {
            alts.Insert(0, "[" + ascii + "]");
        }

        return alts.Count switch
        {
            0 => @"[^\u0000-\uFFFF]",
            // Always one atom, so a quantifier can follow it directly: an ASCII class, or a group.
            1 when nonAscii.IsEmpty => alts[0],
            _ => "(?:" + string.Join("|", alts) + ")",
        };
    }

    private static IEnumerable<(int Lo, int Hi)> SplitByLength(int lo, int hi)
    {
        foreach (var max in (int[])[0x7F, 0x7FF, 0xFFFF, CodePointSet.MaxCodePoint])
        {
            if (lo > hi)
            {
                yield break;
            }

            if (lo <= max)
            {
                yield return (lo, Math.Min(hi, max));
                lo = max + 1;
            }
        }
    }

    /// <summary>
    /// Go regexp/syntax style UTF-8 range compilation; <paramref name="lo"/> and <paramref name="hi"/> encode to the same
    /// length.
    /// </summary>
    private static void Utf8Sequences(int lo, int hi, List<string> alts)
    {
        Span<byte> a = stackalloc byte[4];
        Span<byte> b = stackalloc byte[4];
        var n = new Rune(lo).EncodeToUtf8(a);
        for (var i = 1; i < n; i++)
        {
            var m = (1 << (6 * i)) - 1;
            if ((lo & ~m) != (hi & ~m))
            {
                if ((lo & m) != 0)
                {
                    Utf8Sequences(lo, lo | m, alts);
                    Utf8Sequences((lo | m) + 1, hi, alts);
                    return;
                }

                if ((hi & m) != m)
                {
                    Utf8Sequences(lo, (hi & ~m) - 1, alts);
                    Utf8Sequences(hi & ~m, hi, alts);
                    return;
                }
            }
        }

        new Rune(hi).EncodeToUtf8(b);
        var sb = new StringBuilder();
        for (var k = 0; k < n; k++)
        {
            sb.Append(a[k] == b[k] ? B(a[k]) : $"[{B(a[k])}-{B(b[k])}]");
        }

        alts.Add(sb.ToString());
    }

    // ---- the parser ---------------------------------------------------------------------------------------------------

    private enum Last
    {
        Nothing,
        Atom,
        Assertion,
        Repetition,
    }

    /// <summary>One open group: its SY-104 nodes so far, and the last atom, which a repetition applies to.</summary>
    private sealed class Frame
    {
        public long Nodes;
        public bool HasPending;
        public long PendingNodes;
        public Last Last;
    }

    /// <summary>
    /// A single left-to-right pass with an explicit stack of groups (no recursion, so deep nesting can't overflow the
    /// stack). It counts SY-104's nodes as it goes, saturating above the bound; with <c>emit</c> it also writes the .NET
    /// pattern into one builder. Every emitted atom is a single .NET atom (a class or a group), so a repetition is appended
    /// as is: the pass stays linear however deep the groups nest.
    /// </summary>
    private sealed class Parser
    {
        private const string Escapable = ".^$*+?()[]{}|\\/-";

        private static readonly CodePointSet Digit = CodePointSet.FromRanges([('0', '9')]);
        private static readonly CodePointSet Word = CodePointSet.FromRanges([('0', '9'), ('A', 'Z'), ('_', '_'), ('a', 'z')]);
        private static readonly CodePointSet Space = CodePointSet.FromRanges([(0x09, 0x0D), (0x20, 0x20)]);
        private static readonly CodePointSet NotLineFeed = CodePointSet.Of('\n').Complement();

        private readonly string _pattern;
        private readonly int[] _p;
        private readonly bool _emit;
        private readonly StringBuilder _out = new();
        private bool _ignoreCase;
        private int _i;

        public Parser(string pattern, bool emit, bool ignoreCase)
        {
            _pattern = pattern;
            var runes = new List<int>(pattern.Length);
            foreach (var rune in pattern.EnumerateRunes())
            {
                runes.Add(rune.Value);
            }

            _p = [.. runes];
            _emit = emit;
            _ignoreCase = ignoreCase;
        }

        private bool More => _i < _p.Length;

        private int Peek => _p[_i];

        public GrammarError? Run(out string body, out bool ignoreCase)
        {
            body = "";
            ignoreCase = _ignoreCase;
            if (_pattern.StartsWith("(?i)", StringComparison.Ordinal))
            {
                _ignoreCase = ignoreCase = true;
                _i = 4;
            }

            var stack = new Stack<Frame>();
            stack.Push(new Frame());
            while (More)
            {
                var frame = stack.Peek();
                var c = _p[_i];
                _i++;
                switch (c)
                {
                    case '|':
                        Flush(frame);
                        Write("|");
                        frame.Last = Last.Nothing;
                        break;
                    case '(':
                        if (OpenGroup() is { } groupError)
                        {
                            return groupError;
                        }

                        Write("(?:");
                        stack.Push(new Frame());
                        break;
                    case ')':
                        if (stack.Count == 1)
                        {
                            return Outside("a ')' closes no group");
                        }

                        Flush(frame);
                        stack.Pop();
                        Add(stack.Peek(), ")", frame.Nodes, Last.Atom);
                        break;
                    case '*' or '+' or '?' or '{':
                        if (Repetition(frame, c) is { } repetitionError)
                        {
                            return repetitionError;
                        }

                        break;
                    case '^':
                        Add(frame, @"\A", 1, Last.Assertion);
                        break;
                    case '$':
                        Add(frame, @"\z", 1, Last.Assertion);
                        break;
                    case '.':
                        Add(frame, Set(NotLineFeed), 1, Last.Atom);
                        break;
                    case '[':
                        if (Class(out var set) is { } classError)
                        {
                            return classError;
                        }

                        Add(frame, Set(set), 1, Last.Atom);
                        break;
                    case '\\':
                        if (!More)
                        {
                            return Outside("a trailing '\\' escapes nothing");
                        }

                        if (Peek is 'b' or 'B')
                        {
                            Add(frame, Peek == 'b' ? @"\b" : @"\B", 1, Last.Assertion);
                            _i++;
                            break;
                        }

                        if (Escape(inClass: false, out var escaped, out var single) is { } escapeError)
                        {
                            return escapeError;
                        }

                        Add(frame, Set(single >= 0 ? Literal(single) : escaped), 1, Last.Atom);
                        break;
                    default:
                        // A ']' or '}' that closes nothing is itself (SY-61).
                        Add(frame, Set(Literal(c)), 1, Last.Atom);
                        break;
                }
            }

            if (stack.Count > 1)
            {
                return Outside("a '(' is never closed");
            }

            var top = stack.Pop();
            Flush(top);
            if (top.Nodes > MaxNodes)
            {
                return Outside(string.Create(CultureInfo.InvariantCulture,
                    $"it has more than {MaxNodes} nodes once counted repetitions are expanded (SY-104)"));
            }

            body = _out.ToString();
            return null;
        }

        private GrammarError Outside(string why) =>
            new("RF-22", $"the regex '{_pattern}' is outside the portable subset: {why}");

        /// <summary>After a '(': a plain group, or <c>(?:</c>; any other <c>(?</c> is outside the subset.</summary>
        private GrammarError? OpenGroup()
        {
            if (!At("?"))
            {
                return null;
            }

            if (At("?:"))
            {
                _i += 2;
                return null;
            }

            return Outside(this switch
            {
                _ when At("?i)") => "(?i) is allowed only at the very start of the pattern",
                _ when At("?=") || At("?!") => "lookahead isn't in the subset",
                _ when At("?<=") || At("?<!") => "lookbehind isn't in the subset",
                _ when At("?<") || At("?P<") || At("?'") => "named groups aren't in the subset: use (…) or (?:…)",
                _ when At("?>") => "atomic groups aren't in the subset",
                _ => "inline options other than a leading (?i) aren't in the subset",
            });
        }

        /// <summary>Whether the code points at the current index are the ASCII <paramref name="text"/>.</summary>
        private bool At(string text)
        {
            if (_i + text.Length > _p.Length)
            {
                return false;
            }

            for (var k = 0; k < text.Length; k++)
            {
                if (_p[_i + k] != text[k])
                {
                    return false;
                }
            }

            return true;
        }

        private GrammarError? Repetition(Frame frame, int c)
        {
            long min = 0, max = -1;
            var form = ((char)c).ToString();
            switch (c)
            {
                case '+':
                    min = 1;
                    break;
                case '?':
                    max = 1;
                    break;
                case '{':
                    if (!Count(out min, out max, out form))
                    {
                        return Outside("a '{' that doesn't start a repetition {n}, {n,} or {n,m} (escape it as \\{)");
                    }

                    if (min > MaxCount || max > MaxCount)
                    {
                        return Outside(string.Create(CultureInfo.InvariantCulture, $"repetition counts are at most {MaxCount}"));
                    }

                    if (max >= 0 && max < min)
                    {
                        return Outside($"in {form} the second count is smaller than the first");
                    }

                    break;
            }

            switch (frame.Last)
            {
                case Last.Nothing:
                    return Outside($"the repetition {form} repeats nothing");
                case Last.Assertion:
                    return Outside($"the repetition {form} repeats an anchor or boundary (^, $, \\b, \\B)");
                case Last.Repetition:
                    return Outside($"a repetition {form} follows another repetition "
                        + "(possessive or stacked quantifiers aren't in the subset)");
            }

            var lazy = More && Peek == '?';
            if (lazy)
            {
                _i++;
            }

            // SY-104: {n} is n copies, {n,m} m copies, {n,} n + 1 copies; *, + and ? count as none.
            var copies = c == '{' ? (max < 0 ? min + 1 : max) : 1;
            frame.PendingNodes = Saturate(frame.PendingNodes * copies);
            Write(form);
            if (lazy)
            {
                Write("?");
            }

            frame.Last = Last.Repetition;
            return null;
        }

        /// <summary>After a '{': <c>n}</c>, <c>n,}</c> or <c>n,m}</c>, decimal; false (nothing consumed) otherwise.</summary>
        private bool Count(out long min, out long max, out string form)
        {
            var start = _i;
            min = Number();
            max = min;
            if (min >= 0 && More && Peek == ',')
            {
                _i++;
                max = More && Peek == '}' ? -1 : Number();
                if (max == -1 && !(More && Peek == '}'))
                {
                    max = -2;
                }
            }

            if (min < 0 || max == -2 || !More || Peek != '}')
            {
                _i = start;
                form = "{";
                return false;
            }

            _i++;
            form = string.Concat(_p.AsSpan(start - 1, _i - start + 1).ToArray().Select(c => (char)c));
            return true;
        }

        /// <summary>Decimal digits, capped above <see cref="MaxCount"/>; -1 if there are none.</summary>
        private long Number()
        {
            var start = _i;
            long value = 0;
            while (More && Peek is >= '0' and <= '9')
            {
                value = Math.Min((value * 10) + (Peek - '0'), MaxCount + 1);
                _i++;
            }

            return _i == start ? -1 : value;
        }

        private static long Saturate(long nodes) => Math.Min(nodes, MaxNodes + 1L);

        private void Write(string text)
        {
            if (_emit)
            {
                _out.Append(text);
            }
        }

        /// <summary>A new last atom (or the ')' that ends a group atom); the previous one's nodes are final.</summary>
        private void Add(Frame frame, string text, long nodes, Last kind)
        {
            Flush(frame);
            Write(text);
            frame.HasPending = true;
            frame.PendingNodes = nodes;
            frame.Last = kind;
        }

        private static void Flush(Frame frame)
        {
            if (frame.HasPending)
            {
                frame.Nodes = Saturate(frame.Nodes + frame.PendingNodes);
                frame.HasPending = false;
            }
        }

        private string Set(CodePointSet set) => _emit ? Emit(set) : "";

        /// <summary>A literal code point: under (?i), every code point with the same simple uppercase mapping (CO-12).</summary>
        private CodePointSet Literal(int c) =>
            _emit && _ignoreCase ? SimpleCase.Fold(CodePointSet.Of(c)) : CodePointSet.Of(c);

        /// <summary>
        /// After a '\': a class escape (<c>\d</c> …, its set, <paramref name="single"/> -1) or one code point
        /// (<paramref name="single"/>).
        /// </summary>
        private GrammarError? Escape(bool inClass, out CodePointSet set, out int single)
        {
            set = CodePointSet.Empty;
            single = -1;
            var e = _p[_i++];
            switch (e)
            {
                case 'd':
                    set = Digit;
                    return null;
                case 'D':
                    set = Digit.Complement();
                    return null;
                case 'w':
                    set = Word;
                    return null;
                case 'W':
                    set = Word.Complement();
                    return null;
                case 's':
                    set = Space;
                    return null;
                case 'S':
                    set = Space.Complement();
                    return null;
                case 't':
                    single = '\t';
                    return null;
                case 'n':
                    single = '\n';
                    return null;
                case 'r':
                    single = '\r';
                    return null;
                case 'f':
                    single = '\f';
                    return null;
                case 'v':
                    single = '\v';
                    return null;
                case 'x':
                    single = Hex(2, 2);
                    return single < 0 ? Outside("\\x takes exactly two hex digits (\\x41)") : null;
                case 'u':
                    if (!More || Peek != '{')
                    {
                        return Outside("\\u takes a code point in braces (\\u{42})");
                    }

                    _i++;
                    single = Hex(1, 8);
                    if (single < 0 || !More || Peek != '}')
                    {
                        return Outside("\\u takes a code point in braces (\\u{42})");
                    }

                    _i++;
                    return single > CodePointSet.MaxCodePoint || single is >= 0xD800 and <= 0xDFFF
                        ? Outside("\\u{…} names no Unicode scalar value")
                        : null;
                case < 128 when Escapable.Contains((char)e, StringComparison.Ordinal):
                    single = e;
                    return null;
                case 'b' or 'B' when inClass:
                    return Outside("\\b isn't allowed in a character class");
                case >= '1' and <= '9' or 'k':
                    return Outside("backreferences aren't in the subset");
                case 'A' or 'z' or 'Z' or 'G':
                    return Outside($"\\{(char)e} isn't in the subset: use ^ and $");
                case 'p' or 'P':
                    return Outside("Unicode classes (\\p{…}) aren't in the subset");
                default:
                    return Outside($"the escape \\{char.ConvertFromUtf32(e)} isn't in the subset");
            }
        }

        /// <summary><paramref name="min"/> to <paramref name="max"/> hex digits as a value; -1 if fewer.</summary>
        private int Hex(int min, int max)
        {
            int value = 0, n = 0;
            while (n < max && More && Peek < 128 && char.IsAsciiHexDigit((char)Peek))
            {
                value = (value * 16) + Convert.ToInt32(((char)Peek).ToString(), 16);
                _i++;
                n++;
            }

            return n < min ? -1 : value;
        }

        /// <summary>After a '[': the class's set, folded before it is negated (SY-61); class escapes aren't folded.</summary>
        private GrammarError? Class(out CodePointSet set)
        {
            set = CodePointSet.Empty;
            var negated = More && Peek == '^';
            if (negated)
            {
                _i++;
            }

            var literals = new List<(int, int)>();
            var escapes = new List<(int, int)>();
            var first = true;
            while (true)
            {
                if (!More)
                {
                    return Outside("a '[' is never closed");
                }

                if (Peek == ']')
                {
                    _i++;
                    if (first)
                    {
                        return Outside("an empty class ([] or [^]) isn't in the subset; escape a literal ] as \\]");
                    }

                    break;
                }

                if (ClassAtom(first, out var atom, out var lo) is { } error)
                {
                    return error;
                }

                first = false;
                if (More && Peek == '-' && _i + 1 < _p.Length && _p[_i + 1] != ']')
                {
                    _i++;
                    if (ClassAtom(false, out _, out var hi) is { } rangeError)
                    {
                        return rangeError;
                    }

                    if (lo < 0 || hi < 0)
                    {
                        return Outside("a range can't start or end with a class escape such as \\d");
                    }

                    if (hi < lo)
                    {
                        return Outside("a range's end comes before its start");
                    }

                    literals.Add((lo, hi));
                }
                else if (lo >= 0)
                {
                    literals.Add((lo, lo));
                }
                else
                {
                    escapes.AddRange(atom.Ranges.ToArray());
                }
            }

            if (!_emit)
            {
                return null;
            }

            var folded = CodePointSet.FromRanges(literals);
            if (_ignoreCase)
            {
                folded = SimpleCase.Fold(folded);
            }

            set = folded.Union(CodePointSet.FromRanges(escapes));
            if (negated)
            {
                set = set.Complement();
            }

            return null;
        }

        /// <summary>One class member: a code point (<paramref name="single"/>) or a class escape (its set, single -1).</summary>
        private GrammarError? ClassAtom(bool first, out CodePointSet set, out int single)
        {
            set = CodePointSet.Empty;
            single = -1;
            var c = _p[_i++];
            switch (c)
            {
                case '\\':
                    return More ? Escape(inClass: true, out set, out single) : Outside("a '[' is never closed");
                case '[':
                    return Outside("an unescaped '[' inside a class isn't in the subset (escape it as \\[)");
                case '-' when !first && More && Peek != ']':
                    return Outside("a '-' in a class is literal only first or last; elsewhere it must form a range");
                default:
                    single = c;
                    return null;
            }
        }
    }
}
