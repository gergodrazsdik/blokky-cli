using System.Text;
using Blokky.Core.Ast;

namespace Blokky.Core.Syntax;

/// <summary>A part of a glob segment.</summary>
public enum GlobTokenKind
{
    /// <summary>Literal text (escapes removed).</summary>
    Literal,

    /// <summary><c>*</c>: any run of characters within the segment.</summary>
    Star,

    /// <summary><c>?</c>: one character.</summary>
    Question,

    /// <summary><c>[…]</c> or <c>[!…]</c>: one character from a class.</summary>
    Class,
}

/// <summary>A part of a glob segment.</summary>
/// <param name="Kind">What it matches.</param>
/// <param name="Text">The literal text, for <see cref="GlobTokenKind.Literal"/>.</param>
/// <param name="Set">The class's code points (before negation), for <see cref="GlobTokenKind.Class"/>.</param>
/// <param name="Negated">Whether the class is <c>[!…]</c>.</param>
public sealed record GlobToken(GlobTokenKind Kind, string? Text = null, CodePointSet? Set = null, bool Negated = false);

/// <summary>A <c>/</c>-separated segment of a glob: <c>**</c> (<see cref="Recursive"/>) or its tokens.</summary>
/// <param name="Recursive">Whether the segment is <c>**</c>, zero or more directories (SY-72).</param>
/// <param name="Tokens">The segment's tokens, empty for <c>**</c>.</param>
/// <param name="StartsWithDot">Whether the segment starts with a literal <c>.</c>, which lets it match dot-entries (SY-100).</param>
public sealed record GlobSegment(bool Recursive, IReadOnlyList<GlobToken> Tokens, bool StartsWithDot);

/// <summary>
/// <c>for: { glob }</c> patterns (syntax.md §6.9, SY-69 to SY-74, SY-100 to SY-102): <c>/</c>-separated segments of
/// literals, <c>*</c>, <c>?</c> and classes, <c>**</c> as a whole segment, <c>\</c> escapes, no braces.
/// </summary>
public static class GlobPattern
{
    /// <summary>
    /// Parses <paramref name="text"/> into segments. Null when valid, otherwise the rule of the first mistake: SY-72
    /// (<c>**</c> inside a segment), SY-73 (braces, a trailing backslash), SY-102 (a <c>..</c> segment), SY-70 (an
    /// absolute pattern or an empty segment) or SY-71 (a malformed class).
    /// </summary>
    public static GrammarError? Parse(string text, out IReadOnlyList<GlobSegment> segments)
    {
        ArgumentNullException.ThrowIfNull(text);
        var list = new List<GlobSegment>();
        segments = list;
        if (text.StartsWith('/'))
        {
            return new("SY-70", $"'{text}': a glob is relative to the node's cwd; it can't start with '/'");
        }

        var tokens = new List<GlobToken>();
        var literal = new StringBuilder();
        var segmentStart = 0;
        var startsWithDot = false;
        var i = 0;
        while (true)
        {
            if (i == text.Length || text[i] == '/')
            {
                FlushLiteral(literal, tokens);
                var raw = text.AsSpan(segmentStart, i - segmentStart);
                if (raw.Length == 0)
                {
                    return new("SY-70", $"'{text}': a glob has no empty segments (// or a trailing /)");
                }

                if (raw.SequenceEqual("**"))
                {
                    list.Add(new(true, [], false));
                }
                else
                {
                    if (tokens is [{ Kind: GlobTokenKind.Literal, Text: ".." }])
                    {
                        return new("SY-102", $"'{text}': a '..' segment would leave the cwd subtree");
                    }

                    list.Add(new(false, [.. tokens], startsWithDot));
                }

                if (i == text.Length)
                {
                    return null;
                }

                tokens.Clear();
                i++;
                segmentStart = i;
                startsWithDot = i < text.Length && text[i] == '.';
                continue;
            }

            if (i == segmentStart)
            {
                startsWithDot = text[i] == '.';
            }

            var c = text[i];
            switch (c)
            {
                case '\\':
                    if (i + 1 == text.Length)
                    {
                        return new("SY-73", $"'{text}': a trailing '\\' escapes nothing");
                    }

                    literal.Append(text[i + 1]);
                    i += 2;
                    break;
                case '{' or '}':
                    return new("SY-73", $"'{text}': globs have no braces; escape a literal '{c}' as \\{c}, or use one for per pattern");
                case '*':
                    if (i + 1 < text.Length && text[i + 1] == '*')
                    {
                        var end = text.IndexOf('/', i);
                        var raw = text.AsSpan(segmentStart, (end < 0 ? text.Length : end) - segmentStart);
                        if (!raw.SequenceEqual("**"))
                        {
                            return new("SY-72", $"'{text}': ** is allowed only as a whole segment (docs/**/*.md)");
                        }

                        i += 2;
                        break;
                    }

                    FlushLiteral(literal, tokens);
                    tokens.Add(new(GlobTokenKind.Star));
                    i++;
                    break;
                case '?':
                    FlushLiteral(literal, tokens);
                    tokens.Add(new(GlobTokenKind.Question));
                    i++;
                    break;
                case '[':
                    FlushLiteral(literal, tokens);
                    if (Class(text, ref i, out var token) is { } error)
                    {
                        return error;
                    }

                    tokens.Add(token);
                    break;
                default:
                    literal.Append(c);
                    i++;
                    break;
            }
        }
    }

    private static void FlushLiteral(StringBuilder literal, List<GlobToken> tokens)
    {
        if (literal.Length > 0)
        {
            tokens.Add(new(GlobTokenKind.Literal, literal.ToString()));
            literal.Clear();
        }
    }

    /// <summary>At a '[': <c>[abc]</c>, <c>[a-z]</c>, <c>[!…]</c>, with <c>\</c> escapes; ranges by code point.</summary>
    private static GrammarError? Class(string text, ref int i, out GlobToken token)
    {
        token = new(GlobTokenKind.Class);
        i++;
        var negated = i < text.Length && text[i] == '!';
        if (negated)
        {
            i++;
        }

        var ranges = new List<(int, int)>();
        var first = true;
        while (true)
        {
            if (i == text.Length || text[i] == '/')
            {
                return new("SY-71", $"'{text}': a '[' is never closed within its segment; escape a literal [ as \\[");
            }

            if (text[i] == ']')
            {
                if (first)
                {
                    return new("SY-71", $"'{text}': a class has at least one character; escape a literal ] as \\]");
                }

                i++;
                token = new(GlobTokenKind.Class, Set: CodePointSet.FromRanges(ranges), Negated: negated);
                return null;
            }

            first = false;
            if (Member(text, ref i, out var lo) is { } error)
            {
                return error;
            }

            if (i + 1 < text.Length && text[i] == '-' && text[i + 1] is not (']' or '/'))
            {
                i++;
                if (Member(text, ref i, out var hi) is { } rangeError)
                {
                    return rangeError;
                }

                if (hi < lo)
                {
                    return new("SY-71", $"'{text}': a range's end comes before its start");
                }

                ranges.Add((lo, hi));
            }
            else
            {
                ranges.Add((lo, lo));
            }
        }
    }

    private static GrammarError? Member(string text, ref int i, out int codePoint)
    {
        codePoint = 0;
        if (text[i] == '\\')
        {
            i++;
            if (i == text.Length)
            {
                return new("SY-73", $"'{text}': a trailing '\\' escapes nothing");
            }
        }

        var rune = System.Text.Rune.GetRuneAt(text, i);
        codePoint = rune.Value;
        i += rune.Utf16SequenceLength;
        return null;
    }
}
