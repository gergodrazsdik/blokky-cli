using Blokky.Core.Errors;
using Blokky.Core.Reading.Yaml;

namespace Blokky.Core.Reading;

/// <summary>What the plan reader found.</summary>
/// <param name="Text">The decoded text without its BOM, or null when the encoding pass failed.</param>
/// <param name="Lines">The line map of <paramref name="Text"/>, or null.</param>
/// <param name="Markers">Token-level facts (comments, directives, complex keys).</param>
/// <param name="Events">The YAML events read, up to the first error that ends processing.</param>
/// <param name="Errors">Every plan error found; empty when the plan reads cleanly.</param>
public sealed record PlanReadResult(
    string? Text,
    LineMap? Lines,
    IReadOnlyList<YamlMarker> Markers,
    IReadOnlyList<YamlEvent> Events,
    IReadOnlyList<BlokkyError> Errors)
{
    /// <summary>Whether the plan reads cleanly: valid text and YAML within the subset.</summary>
    public bool Ok => Errors.Count == 0;

    /// <summary>
    /// Whether the events can still be built into a tree, so the later checks add their errors to the reader's (SY-3): the
    /// text is valid YAML, and every error is one the tree can leave out (an anchor, alias or merge key, a tag or directive,
    /// a second document, a repeated key). Encoding, character, cap and syntax errors, rejected keys (SY-17) and a root that
    /// isn't a mapping (SY-32) end processing.
    /// </summary>
    public bool CanContinue =>
        Text is not null
        && Events.Count > 0
        && Events[^1].Kind != YamlEventKind.Error
        && Errors.All(e => e.Code is ErrorCodes.PlanYaml or ErrorCodes.PlanDuplicateKey
            && e.Detail?["rule"]?.GetValue<string>() is "SY-14" or "SY-15" or "SY-16" or "SY-18");
}

/// <summary>
/// The plan reader (architecture.md §1): bytes in, then the encoding pass (<see cref="PlanText"/>), the character lint
/// (<see cref="CharacterLint"/>), and the YAML front end behind <see cref="IYamlFrontEnd"/> with the subset rules of
/// <see cref="YamlSubsetChecker"/>. Encoding and character errors end processing (SY-6, SY-7, SY-10); YAML errors are
/// all collected, up to a syntax error.
/// </summary>
/// <param name="frontEnd">The YAML front end; YamlDotNet's by default.</param>
public sealed class PlanReader(IYamlFrontEnd? frontEnd = null)
{
    private readonly IYamlFrontEnd _frontEnd = frontEnd ?? YamlDotNetFrontEnd.Instance;

    /// <summary>Reads <paramref name="bytes"/>, the plan exactly as given.</summary>
    public PlanReadResult Read(ReadOnlySpan<byte> bytes)
    {
        var (text, encodingError) = PlanText.Decode(bytes);
        if (text is null)
        {
            return new PlanReadResult(null, null, [], [], [encodingError!]);
        }

        var lines = new LineMap(text);
        var characterErrors = CharacterLint.Check(text);
        if (characterErrors.Count > 0)
        {
            return new PlanReadResult(text, lines, [], [], characterErrors);
        }

        var markers = _frontEnd.ScanMarkers(text, lines);
        var events = new List<YamlEvent>();
        var errors = YamlSubsetChecker.Check(markers, _frontEnd.ReadEvents(text, lines), events);
        return new PlanReadResult(text, lines, markers, events, errors);
    }
}
