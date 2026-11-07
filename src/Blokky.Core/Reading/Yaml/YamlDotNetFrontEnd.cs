using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.Core.Tokens;
using CommentToken = YamlDotNet.Core.Tokens.Comment;
using ScalarToken = YamlDotNet.Core.Tokens.Scalar;

namespace Blokky.Core.Reading.Yaml;

/// <summary>
/// The YAML front end on YamlDotNet's <see cref="Scanner"/> and <see cref="Parser"/> only: no deserializer, no
/// representation model, nothing reflection-based (ADR 0008). It guards against the YamlDotNet 18.1.0 defects of tracker
/// F-0025:
/// <list type="number">
/// <item>the scanner can yield <c>Error</c> tokens forever: the token pass stops at the first one, and every token and
/// event loop is bounded (<see cref="StepLimit"/>);</item>
/// <item>malformed input can throw exceptions other than <see cref="YamlException"/>, without marks: every exception but
/// out-of-memory becomes an <see cref="YamlEventKind.Error"/> event, located at the last good event;</item>
/// <item>an empty key in a nested block mapping gives a key event with no value event and a wrong mark: the empty key is
/// found on the tokens (<see cref="YamlMarkerKind.EmptyKey"/>), and consumers don't assume key/value pairs;</item>
/// <item>U+2028 in a plain scalar fails to parse (YAML 1.1 line breaks): the character lint rejects U+2028 and U+2029
/// before this runs (SY-12);</item>
/// <item>a leading U+FEFF becomes part of the first key: the encoding pass strips the UTF-8 BOM before this runs
/// (<see cref="PlanText"/>).</item>
/// </list>
/// Positions come from <c>Mark.Index</c> (a UTF-16 index) through <see cref="LineMap"/>, never from <c>Mark.Line</c> and
/// <c>Mark.Column</c>, which count UTF-16 units and treat U+2028 and U+2029 as line breaks.
/// </summary>
public sealed class YamlDotNetFrontEnd : IYamlFrontEnd
{
    /// <summary>The shared instance; the type holds no state.</summary>
    public static YamlDotNetFrontEnd Instance { get; } = new();

    /// <summary>
    /// The most tokens or events a text can produce. Each consumes text, except a bounded number per character (block
    /// ends, empty scalars), so a loop past this bound is a parser that stopped making progress (defect 1).
    /// </summary>
    internal static long StepLimit(string text) => (8L * text.Length) + 64;

    /// <inheritdoc/>
    public IReadOnlyList<YamlMarker> ScanMarkers(string text, LineMap lines)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(lines);
        var markers = new List<YamlMarker>();
        using var reader = new StringReader(text);
        var scanner = new Scanner(reader, skipComments: false);
        Token? previous = null;
        var limit = StepLimit(text);
        for (long steps = 0; steps < limit; steps++)
        {
            var (token, failed) = NextToken(scanner);
            if (token is null or YamlDotNet.Core.Tokens.Error || failed)
            {
                break;
            }

            var at = lines.At(Index(token.Start));
            switch (token)
            {
                case VersionDirective or TagDirective:
                    markers.Add(new YamlMarker(YamlMarkerKind.Directive, at));
                    break;
                case Key when CharAt(text, token.Start) == '?':
                    markers.Add(new YamlMarker(YamlMarkerKind.ExplicitKey, at));
                    break;
                case Value when !IsKeyEnd(previous, text):
                    markers.Add(new YamlMarker(YamlMarkerKind.EmptyKey, at));
                    break;
                case CommentToken comment:
                    markers.Add(new YamlMarker(YamlMarkerKind.Comment, at, comment.IsInline, comment.Value));
                    break;
            }

            if (token is not CommentToken)
            {
                previous = token;
            }
        }

        return markers;
    }

    /// <inheritdoc/>
    public IEnumerable<YamlEvent> ReadEvents(string text, LineMap lines)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(lines);
        return Iterate(text, lines);
    }

    private static IEnumerable<YamlEvent> Iterate(string text, LineMap lines)
    {
        using var reader = new StringReader(text);
        var parser = new Parser(reader);
        ParsingEvent? last = null;
        var limit = StepLimit(text);
        for (long steps = 0; ; steps++)
        {
            if (steps >= limit)
            {
                yield return Failure(text, lines, last, null, "the YAML parser stopped making progress");
                yield break;
            }

            var (next, failure) = NextEvent(parser);
            if (failure is not null)
            {
                yield return Failure(text, lines, last, failure, null);
                yield break;
            }

            if (next is null)
            {
                yield break;
            }

            last = next;
            if (Convert(next, text, lines) is { } converted)
            {
                yield return converted;
            }
        }
    }

    // Defect 2: YamlDotNet throws YamlException, but also InvalidOperationException, ArgumentException and others.
#pragma warning disable CA1031 // Every library failure is a YAML error of the plan, never a crash.
    private static (Token? Token, bool Failed) NextToken(Scanner scanner)
    {
        try
        {
            return scanner.MoveNext() ? (scanner.Current, false) : (null, false);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return (null, true);
        }
    }

    private static (ParsingEvent? Event, Exception? Failure) NextEvent(Parser parser)
    {
        try
        {
            return parser.MoveNext() ? (parser.Current, null) : (null, null);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return (null, ex);
        }
    }
#pragma warning restore CA1031

    private static YamlEvent? Convert(ParsingEvent e, string text, LineMap lines)
    {
        var start = lines.At(Index(e.Start));
        var end = lines.At(Index(e.End));
        YamlEvent Make(YamlEventKind kind) => new() { Kind = kind, Start = start, NodeStart = start, End = end };

        switch (e)
        {
            case YamlDotNet.Core.Events.DocumentStart d:
                return Make(YamlEventKind.DocumentStart) with { Implicit = d.IsImplicit };
            case YamlDotNet.Core.Events.DocumentEnd d:
                return Make(YamlEventKind.DocumentEnd) with { Implicit = d.IsImplicit };
            case MappingStart m:
                return WithProperties(Make(YamlEventKind.MappingStart), m) with { Flow = m.Style == MappingStyle.Flow };
            case SequenceStart s:
                return WithProperties(Make(YamlEventKind.SequenceStart), s) with { Flow = s.Style == SequenceStyle.Flow };
            case MappingEnd:
                return Make(YamlEventKind.MappingEnd);
            case SequenceEnd:
                return Make(YamlEventKind.SequenceEnd);
            case YamlDotNet.Core.Events.Scalar s:
                var style = Style(s.Style);
                var scalar = WithProperties(Make(YamlEventKind.Scalar), s) with { Value = s.Value, Style = style };
                return style is YamlScalarStyle.Literal or YamlScalarStyle.Folded
                    ? scalar with { Start = lines.At(BlockContentStart(text, Index(s.Start))) }
                    : scalar;
            case YamlDotNet.Core.Events.AnchorAlias a:
                return Make(YamlEventKind.Alias) with { Value = a.Value.Value };
            default:
                return null; // stream start and end, comments
        }
    }

    private static YamlEvent WithProperties(YamlEvent e, NodeEvent node) => e with
    {
        Anchor = node.Anchor.IsEmpty ? null : node.Anchor.Value,
        Tag = node.Tag.IsEmpty ? null : node.Tag.Value,
    };

    private static YamlScalarStyle Style(ScalarStyle style) => style switch
    {
        ScalarStyle.SingleQuoted => YamlScalarStyle.SingleQuoted,
        ScalarStyle.DoubleQuoted => YamlScalarStyle.DoubleQuoted,
        ScalarStyle.Literal => YamlScalarStyle.Literal,
        ScalarStyle.Folded => YamlScalarStyle.Folded,
        _ => YamlScalarStyle.Plain,
    };

    /// <summary>
    /// The error event for a library failure. A quoted scalar followed by more text on its line (<c>exec: "a" b</c>) is
    /// located at its opening quote (SY-1, SY-89), not where the parser gave up.
    /// </summary>
    private static YamlEvent Failure(string text, LineMap lines, ParsingEvent? last, Exception? failure, string? message)
    {
        int index;
        if (failure is YamlException yaml)
        {
            index = Index(yaml.Start);
            message = $"invalid YAML: {Describe(yaml)}";
        }
        else
        {
            index = last is null ? 0 : Index(last.End);
            message ??= $"invalid YAML (the parser failed with {failure!.GetType().Name})";
        }

        string? rule = null;
        if (last is YamlDotNet.Core.Events.Scalar { Style: ScalarStyle.SingleQuoted or ScalarStyle.DoubleQuoted } quoted
            && lines.At(Index(quoted.Start)).Line == lines.At(index).Line)
        {
            index = Index(quoted.Start);
            rule = "SY-89";
            message = "a quoted scalar is followed by more text: YAML reads the quotes as the whole value; quote the whole "
                + "value ('\"a\" b'), or use the list form";
        }
        else if (CharAt(text, index) == '"' && failure is YamlException && message.Contains("escape", StringComparison.Ordinal))
        {
            rule = "SY-89";
            message += "; in double quotes a backslash starts an escape: single-quote the value instead ('^\\d+$')";
        }

        var at = lines.At(index);
        return new YamlEvent { Kind = YamlEventKind.Error, Start = at, NodeStart = at, End = at, Value = message, Rule = rule };
    }

    /// <summary>YamlDotNet's message without its own <c>(Line: …)</c> prefix, whose positions aren't SY-1's.</summary>
    private static string Describe(YamlException ex)
    {
        var message = ex.Message;
        var cut = message.StartsWith("(Line:", StringComparison.Ordinal) ? message.IndexOf("): ", StringComparison.Ordinal) : -1;
        message = cut < 0 ? message : message[(cut + 3)..];
        var newline = message.IndexOf('\n', StringComparison.Ordinal);
        return newline < 0 ? message : message[..newline];
    }

    /// <summary>Whether a <c>:</c> after <paramref name="previous"/> ends a key; otherwise its key is empty (SY-17).</summary>
    /// <remarks>In flow collections YamlDotNet puts a zero-length scalar token before such a <c>:</c>.</remarks>
    private static bool IsKeyEnd(Token? previous, string text) =>
        (previous is ScalarToken && previous.End.Index > previous.Start.Index)
        || previous is FlowSequenceEnd or FlowMappingEnd or Anchor or Tag or YamlDotNet.Core.Tokens.AnchorAlias
        || (previous is Key && CharAt(text, previous.Start) == '?'); // `? ` with no key: reported once, as a complex key

    /// <summary>SY-1: a block scalar is located at the first character of its first content line.</summary>
    internal static int BlockContentStart(string text, int indicator)
    {
        var i = indicator;
        while (i < text.Length && text[i] is not ('\n' or '\r'))
        {
            i++;
        }

        while (i < text.Length)
        {
            i += text[i] == '\r' && i + 1 < text.Length && text[i + 1] == '\n' ? 2 : 1;
            var j = i;
            while (j < text.Length && text[j] == ' ')
            {
                j++;
            }

            if (j < text.Length && text[j] is not ('\n' or '\r'))
            {
                return j;
            }

            i = j;
        }

        return indicator;
    }

    private static char CharAt(string text, Mark mark) => CharAt(text, Index(mark));

    private static char CharAt(string text, int index) => index >= 0 && index < text.Length ? text[index] : '\0';

    private static int Index(Mark mark) => (int)Math.Clamp(mark.Index, 0, int.MaxValue);
}
