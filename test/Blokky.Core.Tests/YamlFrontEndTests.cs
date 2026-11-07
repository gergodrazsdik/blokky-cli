using Blokky.Core.Reading;
using Blokky.Core.Reading.Yaml;

namespace Blokky.Core.Tests;

/// <summary>The YamlDotNet adapter: Blokky's events and positions, and the guards for the defects of tracker F-0025.</summary>
public class YamlFrontEndTests
{
    private static readonly YamlDotNetFrontEnd FrontEnd = YamlDotNetFrontEnd.Instance;

    private static List<YamlEvent> Events(string text) => [.. FrontEnd.ReadEvents(text, new LineMap(text))];

    private static IReadOnlyList<YamlMarker> Markers(string text) => FrontEnd.ScanMarkers(text, new LineMap(text));

    [Test]
    public async Task Every_scalar_is_a_string_with_its_style_and_tag()
    {
        var scalars = Events("a: on\nb: '010'\nc: \"1e3\"\nd: ~\ne: !!str x\n").Where(e => e.Kind == YamlEventKind.Scalar).ToList();

        await Assert.That(string.Join(" ", scalars.Select(s => s.Value))).IsEqualTo("a on b 010 c 1e3 d ~ e x");
        await Assert.That(scalars[3].Style).IsEqualTo(YamlScalarStyle.SingleQuoted);
        await Assert.That(scalars[5].Style).IsEqualTo(YamlScalarStyle.DoubleQuoted);
        await Assert.That(scalars[7].Style).IsEqualTo(YamlScalarStyle.Plain);
        await Assert.That(scalars[9].Tag).IsEqualTo("tag:yaml.org,2002:str");
        await Assert.That(scalars[7].Tag).IsNull();
    }

    [Test]
    public async Task Positions_follow_sy1_for_quoted_and_block_scalars_and_flow_collections()
    {
        var events = Events("x: [\U0001F600, 'q']\nscript: |\n\n    echo hi\nlog: >-\n  folded\n");
        var quoted = events.Single(e => e.Value == "q");
        var literal = events.Single(e => e.Style == YamlScalarStyle.Literal);
        var folded = events.Single(e => e.Style == YamlScalarStyle.Folded);
        var flow = events.Single(e => e.Kind == YamlEventKind.SequenceStart);

        await Assert.That(quoted.Start).IsEqualTo(new PlanPosition(1, 8));
        await Assert.That(literal.Start).IsEqualTo(new PlanPosition(4, 5));
        await Assert.That(literal.NodeStart).IsEqualTo(new PlanPosition(2, 9));
        await Assert.That(literal.Value).IsEqualTo("\necho hi\n");
        await Assert.That(folded.Start).IsEqualTo(new PlanPosition(6, 3));
        await Assert.That(flow.Flow).IsTrue();
        await Assert.That(flow.Start).IsEqualTo(new PlanPosition(1, 4));
    }

    [Test]
    public async Task Comments_are_markers_and_inline_ones_are_flagged()
    {
        var comments = Markers("# head\nexec: a b # tidy\n").Where(m => m.Kind == YamlMarkerKind.Comment).ToList();

        await Assert.That(comments.Count).IsEqualTo(2);
        await Assert.That(comments[0].Inline).IsFalse();
        await Assert.That(comments[1].Inline).IsTrue();
        await Assert.That(comments[1].Position).IsEqualTo(new PlanPosition(2, 11));
        await Assert.That(comments[1].Text).IsEqualTo("tidy");
    }

    [Test]
    public async Task Defect_1_an_endless_error_token_stream_is_cut_off()
    {
        const string Text = "log: a: b\n";

        var markers = Markers(Text);
        var events = Events(Text);

        await Assert.That(markers.Count).IsLessThan(10);
        await Assert.That(events[^1].Kind).IsEqualTo(YamlEventKind.Error);
        await Assert.That(events.Count(e => e.Kind == YamlEventKind.Error)).IsEqualTo(1);
    }

    [Test]
    [Arguments("exec: [a, b\nname: x\n")]
    [Arguments("log: !!st[r hello\n")]
    [Arguments("a: [\n")]
    [Arguments("{a: b}}\n")]
    [Arguments("- a\nb: c\n")]
    [Arguments("a:\n\t- b\n")]
    [Arguments("\"\\q\"\n")]
    public async Task Defect_2_any_parser_failure_is_one_error_event_never_an_exception(string text)
    {
        var events = Events(text);

        await Assert.That(events[^1].Kind).IsEqualTo(YamlEventKind.Error);
        await Assert.That(events[^1].Value).StartsWith("invalid YAML");
        await Assert.That(events[^1].Value).DoesNotContain("Line:");
        await Assert.That(events.Count(e => e.Kind == YamlEventKind.Error)).IsEqualTo(1);
    }

    [Test]
    [Arguments("exec: [a, b\nname: x\n", "InvalidOperationException: Operation is not valid")]
    [Arguments("log: !!st[r hello\n", "ArgumentException: Global tags must be valid URIs")]
    public async Task Defect_2_a_failure_that_is_not_a_yaml_error_keeps_its_type_and_message(string text, string expected)
    {
        await Assert.That(Events(text)[^1].Value).Contains(expected);
    }

    [Test]
    public async Task Defect_2_the_token_pass_records_where_it_failed()
    {
        var failure = Markers("exec: [a, b\nname: x\n")[^1];

        await Assert.That(failure.Kind).IsEqualTo(YamlMarkerKind.ScanFailed);
        await Assert.That(failure.Text).Contains("InvalidOperationException");
    }

    [Test]
    public async Task The_token_pass_stops_past_the_depth_cap()
    {
        var markers = Markers(new string('[', YamlSubsetChecker.MaxDepth + 1) + " # deep\n");

        await Assert.That(markers.Count).IsEqualTo(0);
        await Assert.That(Markers(new string('[', YamlSubsetChecker.MaxDepth) + " # deep\n").Count).IsEqualTo(1);
    }

    [Test]
    public async Task Defect_3_an_empty_nested_key_is_found_on_the_tokens()
    {
        var markers = Markers("x:\n  : a\n");

        await Assert.That(markers.Single().Kind).IsEqualTo(YamlMarkerKind.EmptyKey);
        await Assert.That(markers.Single().Position).IsEqualTo(new PlanPosition(2, 3));
        await Assert.That(string.Join(" ", Markers("x:\n  : a\n  : b\n").Select(m => m.Position)))
            .IsEqualTo("2:3 3:3");
    }

    [Test]
    public async Task Defect_4_u2028_in_a_plain_scalar_fails_in_the_library_without_throwing()
    {
        // The reader never gets here: the character lint rejects U+2028 first (PlanReaderTests).
        var events = Events("log: a\u2028b\n");

        await Assert.That(events.Count).IsGreaterThan(0);
    }

    [Test]
    public async Task Defect_5_a_leading_feff_given_to_the_library_would_join_the_first_key()
    {
        // Documents why PlanText strips the BOM: the reader never passes it on (PlanReaderTests).
        var key = Events("\uFEFFa: b\n").First(e => e.Kind == YamlEventKind.Scalar);

        await Assert.That(key.Value).IsEqualTo("\uFEFFa");
    }

    [Test]
    public async Task A_quoted_scalar_followed_by_text_is_located_at_its_opening_quote()
    {
        var error = Events("blokky: 1.0\nexec: \"blokky-testkit\" exit 0\n")[^1];

        await Assert.That(error.Kind).IsEqualTo(YamlEventKind.Error);
        await Assert.That(error.Start).IsEqualTo(new PlanPosition(2, 7));
        await Assert.That(error.Rule).IsEqualTo("SY-89");
    }

    [Test]
    public async Task An_invalid_escape_in_double_quotes_is_the_sy89_pitfall_at_the_quote()
    {
        var error = Events("a: { b: \"^\\d+$\" }\n")[^1];

        await Assert.That(error.Kind).IsEqualTo(YamlEventKind.Error);
        await Assert.That(error.Start).IsEqualTo(new PlanPosition(1, 9));
        await Assert.That(error.Rule).IsEqualTo("SY-89");
    }

    [Test]
    public async Task Loops_are_bounded_by_the_text_length()
    {
        await Assert.That(YamlDotNetFrontEnd.StepLimit("")).IsEqualTo(64);
        await Assert.That(YamlDotNetFrontEnd.StepLimit("abcd")).IsEqualTo(96);
    }

    [Test]
    public async Task Events_can_be_read_lazily_and_abandoned()
    {
        var first = FrontEnd.ReadEvents("a: [1, 2]\n", new LineMap("a: [1, 2]\n")).Take(2).ToList();

        await Assert.That(string.Join(" ", first.Select(e => e.Kind))).IsEqualTo("DocumentStart MappingStart");
    }
}
