namespace Blokky.Core.Reading.Yaml;

/// <summary>
/// The one seam between Blokky and a YAML library (ADR 0008): nothing else in Core sees the library's types. It reports
/// what the text says, with positions from <see cref="LineMap"/>; the language's rules on top of it (the YAML subset) are
/// <see cref="YamlSubsetChecker"/>'s.
/// </summary>
public interface IYamlFrontEnd
{
    /// <summary>
    /// Token-level facts: directives, explicit and empty keys, comments. Stops at a syntax error with a
    /// <see cref="YamlMarkerKind.ScanFailed"/> marker (<see cref="ReadEvents"/> reports the error), and once the nesting is
    /// deeper than the depth cap (the event stream reaches the cap too).
    /// </summary>
    IReadOnlyList<YamlMarker> ScanMarkers(string text, LineMap lines);

    /// <summary>
    /// The event stream, lazily, so a consumer can stop early (the depth cap). Stream start and end aren't included. Any
    /// failure of the library ends the stream with one <see cref="YamlEventKind.Error"/> event; it never throws (except
    /// out of memory) and never loops forever.
    /// </summary>
    IEnumerable<YamlEvent> ReadEvents(string text, LineMap lines);
}
