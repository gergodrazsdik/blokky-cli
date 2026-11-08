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
    /// <remarks>
    /// The pass stops once the collections open at a token are nested deeper than <see cref="YamlSubsetChecker.MaxDepth"/>:
    /// every collection token is a collection event too, so the event pass reaches the depth cap (SY-9) by then, and the cap
    /// ends processing with no other error. Without the stop, a text of <c>[</c> would be scanned to its end, and
    /// YamlDotNet's scanner takes time quadratic in the flow depth.
    /// </remarks>
    public IReadOnlyList<YamlMarker> ScanMarkers(string text, LineMap lines)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(lines);
        var markers = new List<YamlMarker>();
        using var reader = new StringReader(text);
        var scanner = new Scanner(reader, skipComments: false);
        Token? previous = null;
        var depth = 0;

        // Per open collection, whether a key has begun (a Key token) and its ':' hasn't come yet; the outermost entry is the
        // stream's. A ':' that ends no key has an empty key (SY-17), including one after a value in a block mapping.
        var keyOpen = new Stack<bool>();
        keyOpen.Push(false);
        var limit = StepLimit(text);
        for (long steps = 0; ; steps++)
        {
            if (steps >= limit)
            {
                markers.Add(ScanFailure(lines, previous, "the YAML scanner stopped making progress"));
                break;
            }

            var (token, failure) = NextToken(scanner);
            if (failure is not null)
            {
                markers.Add(ScanFailure(lines, previous, failure));
                break;
            }

            if (token is null)
            {
                break;
            }

            if (token is YamlDotNet.Core.Tokens.Error error)
            {
                markers.Add(new YamlMarker(YamlMarkerKind.ScanFailed, lines.At(Index(token.Start)), Text: error.Value));
                break;
            }

            var at = lines.At(Index(token.Start));
            switch (token)
            {
                case VersionDirective or TagDirective:
                    markers.Add(new YamlMarker(YamlMarkerKind.Directive, at));
                    break;
                case Key:
                    if (CharAt(text, token.Start) == '?')
                    {
                        markers.Add(new YamlMarker(YamlMarkerKind.ExplicitKey, at));
                    }

                    keyOpen.Pop();
                    keyOpen.Push(true);
                    break;
                case Value:
                    if (!keyOpen.Peek() || !IsKeyEnd(previous, text))
                    {
                        markers.Add(new YamlMarker(YamlMarkerKind.EmptyKey, at));
                    }

                    keyOpen.Pop();
                    keyOpen.Push(false);
                    break;
                case CommentToken comment:
                    markers.Add(new YamlMarker(YamlMarkerKind.Comment, at, comment.IsInline, comment.Value));
                    break;
                case BlockEntry:
                    markers.Add(new YamlMarker(YamlMarkerKind.SequenceEntry, at));
                    break;
                case FlowSequenceStart or FlowMappingStart or BlockSequenceStart or BlockMappingStart:
                    if (++depth > YamlSubsetChecker.MaxDepth)
                    {
                        return markers;
                    }

                    keyOpen.Push(false);
                    break;
                case FlowSequenceEnd or FlowMappingEnd or BlockEnd:
                    depth = Math.Max(depth - 1, 0);
                    if (keyOpen.Count > 1)
                    {
                        keyOpen.Pop();
                    }

                    break;
            }

            if (token is not CommentToken)
            {
                previous = token;
            }
        }

        return markers;
    }

    /// <summary>The marker for a scanner failure, located at the last good token (exceptions other than YAML's have no mark).</summary>
    private static YamlMarker ScanFailure(LineMap lines, Token? previous, Exception failure) =>
        failure is YamlException yaml
            ? new YamlMarker(YamlMarkerKind.ScanFailed, lines.At(Index(yaml.Start)), Text: Describe(yaml))
            : ScanFailure(lines, previous, $"the YAML scanner failed with {failure.GetType().Name}: {FirstLine(failure.Message)}");

    private static YamlMarker ScanFailure(LineMap lines, Token? previous, string message) =>
        new(YamlMarkerKind.ScanFailed, lines.At(previous is null ? 0 : Index(previous.End)), Text: message);

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

        // Whether each open collection is in flow style; the flow ones are counted in `flow`.
        var open = new Stack<bool>();
        var flow = 0;
        var limit = StepLimit(text);
        for (long steps = 0; ; steps++)
        {
            if (steps >= limit)
            {
                yield return Failure(text, lines, last, flow > 0, null, "the YAML parser stopped making progress");
                yield break;
            }

            var (next, failure) = NextEvent(parser);
            if (failure is not null)
            {
                yield return Failure(text, lines, last, flow > 0, failure, null);
                yield break;
            }

            if (next is null)
            {
                yield break;
            }

            last = next;
            switch (next)
            {
                case MappingStart or SequenceStart:
                    var isFlow = next is MappingStart { Style: MappingStyle.Flow } or SequenceStart { Style: SequenceStyle.Flow };
                    open.Push(isFlow);
                    flow += isFlow ? 1 : 0;
                    break;
                case MappingEnd or SequenceEnd when open.Count > 0:
                    flow -= open.Pop() ? 1 : 0;
                    break;
            }

            if (Convert(next, text, lines) is { } converted)
            {
                yield return converted;
            }
        }
    }

    // Defect 2: YamlDotNet throws YamlException, but also InvalidOperationException, ArgumentException and others.
#pragma warning disable CA1031 // Every library failure is a YAML error of the plan, never a crash.
    private static (Token? Token, Exception? Failure) NextToken(Scanner scanner)
    {
        try
        {
            return scanner.MoveNext() ? (scanner.Current, null) : (null, null);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return (null, ex);
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
    /// The error event for a library failure. A quoted scalar followed by more text on its line (<c>exec: "a" b</c>), where
    /// the parser gave up at that text, outside flow collections, is located at its opening quote (SY-1, SY-89).
    /// </summary>
    private static YamlEvent Failure(string text, LineMap lines, ParsingEvent? last, bool inFlow, Exception? failure,
        string? message)
    {
        int index;
        if (failure is YamlException yaml)
        {
            index = Index(yaml.Start);
            message = $"invalid YAML: {Describe(yaml)}";
        }
        else
        {
            // Defect 2: not a YAML error, so no mark; the type and message say what failed.
            index = last is null ? 0 : Index(last.End);
            message ??= $"invalid YAML (the parser failed with {failure!.GetType().Name}: {FirstLine(failure.Message)})";
        }

        string? rule = null;
        if (last is YamlDotNet.Core.Events.Scalar { Style: ScalarStyle.SingleQuoted or ScalarStyle.DoubleQuoted } quoted
            && !inFlow
            && failure is YamlException
            && index == TextAfter(text, Index(quoted.End)))
        {
            index = Index(quoted.Start);
            rule = "SY-89";
            message = quoted.Style == ScalarStyle.DoubleQuoted
                ? "a quoted scalar is followed by more text: YAML reads the quotes as the whole value; quote the whole value in "
                    + "single quotes ('\"a\" b'), or use the list form"
                : "a quoted scalar is followed by more text: YAML reads the quotes as the whole value; quote the whole value in "
                    + "double quotes (\"'a' b\"), or use the list form";
        }
        else if (CharAt(text, index) == '"' && failure is YamlException && message.Contains("escape", StringComparison.Ordinal))
        {
            rule = "SY-89";
            message += "; in double quotes a backslash starts an escape: single-quote the value instead ('^\\d+$')";
        }

        var at = lines.At(index);
        return new YamlEvent { Kind = YamlEventKind.Error, Start = at, NodeStart = at, End = at, Value = message, Rule = rule };
    }

    /// <summary>
    /// The index of the first character after <paramref name="end"/> that isn't a space or tab, if it is more text on the
    /// same line (not a comment or a line break); else -1.
    /// </summary>
    private static int TextAfter(string text, int end)
    {
        var i = end;
        while (i < text.Length && text[i] is (' ' or '\t'))
        {
            i++;
        }

        return i < text.Length && text[i] is not ('#' or '\n' or '\r') ? i : -1;
    }

    /// <summary>YamlDotNet's message without its own <c>(Line: …)</c> prefix, whose positions aren't SY-1's.</summary>
    private static string Describe(YamlException ex)
    {
        var message = ex.Message;
        var cut = message.StartsWith("(Line:", StringComparison.Ordinal) ? message.IndexOf("): ", StringComparison.Ordinal) : -1;
        return FirstLine(cut < 0 ? message : message[(cut + 3)..]);
    }

    private static string FirstLine(string message)
    {
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
