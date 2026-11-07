namespace Blokky.Core.Reading.Yaml;

/// <summary>The kinds of event in Blokky's YAML event stream.</summary>
public enum YamlEventKind
{
    /// <summary>A document begins (<c>---</c>, or implicitly at its first content).</summary>
    DocumentStart,

    /// <summary>A document ends.</summary>
    DocumentEnd,

    /// <summary>A mapping begins.</summary>
    MappingStart,

    /// <summary>A mapping ends.</summary>
    MappingEnd,

    /// <summary>A sequence begins.</summary>
    SequenceStart,

    /// <summary>A sequence ends.</summary>
    SequenceEnd,

    /// <summary>A scalar: always a string (SY-23), with its style and tag.</summary>
    Scalar,

    /// <summary>An alias (<c>*name</c>); <see cref="YamlEvent.Value"/> is the anchor name.</summary>
    Alias,

    /// <summary>The text isn't valid YAML: always the last event; <see cref="YamlEvent.Value"/> is the message.</summary>
    Error,
}

/// <summary>How a scalar is written.</summary>
public enum YamlScalarStyle
{
    /// <summary>Not a scalar.</summary>
    None,

    /// <summary>Unquoted.</summary>
    Plain,

    /// <summary><c>'…'</c>.</summary>
    SingleQuoted,

    /// <summary><c>"…"</c>.</summary>
    DoubleQuoted,

    /// <summary>A <c>|</c> block scalar.</summary>
    Literal,

    /// <summary>A <c>&gt;</c> block scalar.</summary>
    Folded,
}

/// <summary>
/// One event of the plan's YAML, independent of the parser library. Positions come from Blokky's own line map (SY-1).
/// </summary>
public sealed record YamlEvent
{
    /// <summary>What happened.</summary>
    public required YamlEventKind Kind { get; init; }

    /// <summary>
    /// Where errors about this node are located (SY-1): a quoted scalar at its opening quote, a block scalar at the first
    /// character of its first content line, a collection at its first token.
    /// </summary>
    public required PlanPosition Start { get; init; }

    /// <summary>Where the node's text begins, its anchor or tag included. Equals <see cref="Start"/> except for block scalars.</summary>
    public required PlanPosition NodeStart { get; init; }

    /// <summary>Just past the event's text.</summary>
    public required PlanPosition End { get; init; }

    /// <summary>A scalar's string, an alias's anchor name, or an error's message.</summary>
    public string? Value { get; init; }

    /// <summary>A scalar's style.</summary>
    public YamlScalarStyle Style { get; init; }

    /// <summary>A collection written in flow style (<c>[…]</c>, <c>{…}</c>).</summary>
    public bool Flow { get; init; }

    /// <summary>The node's explicit tag as the parser resolved it (<c>!!str</c> is <c>tag:yaml.org,2002:str</c>), or null.</summary>
    public string? Tag { get; init; }

    /// <summary>The node's anchor name, or null.</summary>
    public string? Anchor { get; init; }

    /// <summary>A document start or end without its marker (<c>---</c>, <c>...</c>).</summary>
    public bool Implicit { get; init; }

    /// <summary>For an error: the rule it reports as <c>detail.rule</c>, when one applies (SY-89's quoting pitfalls).</summary>
    public string? Rule { get; init; }

    /// <summary>Whether this is a node: a scalar, an alias or the start of a collection.</summary>
    public bool IsNode => Kind is YamlEventKind.Scalar or YamlEventKind.Alias or YamlEventKind.MappingStart or YamlEventKind.SequenceStart;
}

/// <summary>Facts the event stream doesn't carry, found on the scanner's tokens.</summary>
public enum YamlMarkerKind
{
    /// <summary>A <c>%YAML</c> or <c>%TAG</c> directive (SY-15).</summary>
    Directive,

    /// <summary>The <c>?</c> complex-key indicator (SY-17).</summary>
    ExplicitKey,

    /// <summary>A <c>:</c> with no key before it, which YAML reads as a null key (SY-17).</summary>
    EmptyKey,

    /// <summary>A comment; <see cref="YamlMarker.Inline"/> when it follows content on its line (SY-89).</summary>
    Comment,

    /// <summary>
    /// The token pass failed here, so no markers follow; <see cref="YamlMarker.Text"/> says why. The event pass reports a
    /// syntax error at the same place; when it doesn't, the markers after it would be lost silently, so it is an error.
    /// </summary>
    ScanFailed,
}

/// <summary>A token-level fact with its position.</summary>
/// <param name="Kind">What was found.</param>
/// <param name="Position">Where its token starts.</param>
/// <param name="Inline">For a comment: it follows other content on its line.</param>
/// <param name="Text">For a comment: its text after <c>#</c>; for a failed scan: why.</param>
public sealed record YamlMarker(YamlMarkerKind Kind, PlanPosition Position, bool Inline = false, string? Text = null);
