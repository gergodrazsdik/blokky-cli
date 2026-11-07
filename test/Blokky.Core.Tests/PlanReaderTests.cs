using System.Text;
using Blokky.Core.Errors;
using Blokky.Core.Reading;
using Blokky.Core.Reading.Yaml;

namespace Blokky.Core.Tests;

/// <summary>The reader end to end: the encoding pass, the character lint and the YAML subset (SY-9, SY-14 to SY-18, SY-32).</summary>
public class PlanReaderTests
{
    private static PlanReadResult Read(string text) => new PlanReader().Read(Encoding.UTF8.GetBytes(text));

    private static string Summary(PlanReadResult result) =>
        string.Join("; ", result.Errors.Select(e => $"{e.Code} {e.Detail?["rule"]?.GetValue<string>()} {e.Pos}"));

    [Test]
    [Arguments("blokky: 1.0\nlog: hello\n")]
    [Arguments("---\nblokky: 1.0\nlog: hello\n")]
    [Arguments("blokky: 1.0\nlog: hello\n...\n")]
    [Arguments("blokky: 1.0\nlog: hello\n...\n# only a comment\n")]
    [Arguments("blokky: 1.0\nlog: '<<'\nenv: { '<<': x }\n")]
    [Arguments("blokky: 1.0\ndo:\n  - log: a\n  - log: a\n")]
    [Arguments("blokky: 1.0\nlog: a # x & * ! ? %\n")]
    [Arguments("blokky: 1.0\nlog: 'a: b, &c *d !e'\n")]
    [Arguments("blokky: 1.0\ntimeout:\nexec: [a]\n")]
    [Arguments("blokky: 1.0\nenv: {}\ndo: []\n")]
    public async Task Plans_within_the_subset_read_cleanly(string plan)
    {
        var result = Read(plan);

        await Assert.That(Summary(result)).IsEqualTo("");
        await Assert.That(result.Events.Count).IsGreaterThan(0);
    }

    [Test]
    [Arguments("name: &t x\nlog: *t\n", "BK_PLAN_YAML SY-14 1:7; BK_PLAN_YAML SY-14 2:6")]
    [Arguments("do:\n  - <<: { name: x }\n    log: a\n", "BK_PLAN_YAML SY-14 2:5")]
    [Arguments("a: &x [b]\n", "BK_PLAN_YAML SY-14 1:4")]
    [Arguments("name: !custom x\nlog: !!str y\nz: ! w\n", "BK_PLAN_YAML SY-15 1:7; BK_PLAN_YAML SY-15 2:6; BK_PLAN_YAML SY-15 3:4")]
    [Arguments("%YAML 1.2\n---\na: b\n", "BK_PLAN_YAML SY-15 1:1")]
    [Arguments("%TAG !e! tag:example.com,2000:\n---\na: b\n", "BK_PLAN_YAML SY-15 1:1")]
    [Arguments("a: b\n---\nc: d\n", "BK_PLAN_YAML SY-16 2:1")]
    [Arguments("a: b\n...\nc: d\n", "BK_PLAN_YAML SY-16 3:1")]
    [Arguments("a: b\n? c\n: d\n", "BK_PLAN_YAML SY-17 2:1")]
    [Arguments("a: { ? b : c }\n", "BK_PLAN_YAML SY-17 1:6")]
    [Arguments("a: b\n? [c]\n: d\n", "BK_PLAN_YAML SY-17 2:1")]
    [Arguments("a: b\n: c\n", "BK_PLAN_YAML SY-17 2:1")]
    [Arguments("a: { : c }\n", "BK_PLAN_YAML SY-17 1:6")]
    [Arguments("a: [ : c ]\n", "BK_PLAN_YAML SY-17 1:6")]
    [Arguments("- : c\n", "BK_PLAN_YAML SY-17 1:3; BK_PLAN_VALUE SY-32 1:1")]
    [Arguments("x:\n  : a\n", "BK_PLAN_YAML SY-17 2:3")]
    [Arguments("a: b\n[c]: d\n", "BK_PLAN_YAML SY-17 2:1")]
    [Arguments("a: b\n{c: d}: e\n", "BK_PLAN_YAML SY-17 2:1")]
    [Arguments("a: 1\nb: 2\na: 3\n", "BK_PLAN_DUPLICATE_KEY SY-18 3:1")]
    [Arguments("do:\n  - log: a\n    'log': b\n", "BK_PLAN_DUPLICATE_KEY SY-18 3:5")]
    [Arguments("env: { A: 1, \"A\": 2, A: 3 }\n", "BK_PLAN_DUPLICATE_KEY SY-18 1:14; BK_PLAN_DUPLICATE_KEY SY-18 1:22")]
    [Arguments("- a\n- b\n", "BK_PLAN_VALUE SY-32 1:1")]
    [Arguments("[a]\n", "BK_PLAN_VALUE SY-32 1:1")]
    [Arguments("hello\n", "BK_PLAN_VALUE SY-32 1:1")]
    [Arguments("", "BK_PLAN_VALUE SY-32 1:1")]
    [Arguments("# nothing\n", "BK_PLAN_VALUE SY-32 1:1")]
    [Arguments("---\n", "BK_PLAN_VALUE SY-32 1:1")]
    public async Task Yaml_subset_errors_carry_code_rule_and_position(string plan, string expected)
    {
        await Assert.That(Summary(Read(plan))).IsEqualTo(expected);
    }

    [Test]
    public async Task Yaml_errors_are_all_collected_up_to_a_syntax_error()
    {
        var result = Read("%YAML 1.2\n---\na: &x 1\na: !t 2\nb: *x\nc: \"d\" e\nf: &y g\n");

        await Assert.That(Summary(result)).IsEqualTo(
            "BK_PLAN_YAML SY-15 1:1; BK_PLAN_YAML SY-14 3:4; BK_PLAN_DUPLICATE_KEY SY-18 4:1; BK_PLAN_YAML SY-15 4:4; "
            + "BK_PLAN_YAML SY-14 5:4; BK_PLAN_YAML SY-89 6:4");
    }

    [Test]
    public async Task A_syntax_error_without_a_known_pitfall_has_no_rule()
    {
        var error = Read("a: [b\n").Errors.Single();

        await Assert.That(error.Code).IsEqualTo(ErrorCodes.PlanYaml);
        await Assert.That(error.Detail).IsNull();
    }

    [Test]
    public async Task Nesting_up_to_128_levels_is_allowed_and_deeper_ends_processing()
    {
        // The root mapping is level 1, so 127 flow sequences under it reach 128.
        static string Plan(int sequences) => $"&a x: {new string('[', sequences)}{new string(']', sequences)}\n";

        await Assert.That(Summary(Read(Plan(127)))).IsEqualTo("BK_PLAN_YAML SY-14 1:1");
        var deep = Read(Plan(128));
        await Assert.That(Summary(deep)).IsEqualTo("BK_PLAN_TOO_LARGE SY-9 1:134");
        await Assert.That(deep.Events.Count).IsLessThan(140);
    }

    [Test]
    public async Task Character_errors_end_processing_before_yaml_defect_4()
    {
        var result = Read("blokky: 1.0\nlog: a\u2028b\n: broken\n");

        await Assert.That(Summary(result)).IsEqualTo("BK_PLAN_CHARACTER SY-12 2:7");
        await Assert.That(result.Events.Count).IsEqualTo(0);
    }

    [Test]
    public async Task Encoding_errors_end_processing()
    {
        var result = new PlanReader().Read([0x61, 0x3A, 0x20, 0xFF, 0x07, 0x0A, 0x3A]);

        await Assert.That(Summary(result)).IsEqualTo("BK_PLAN_ENCODING SY-7 1:4");
        await Assert.That(result.Text).IsNull();
    }

    [Test]
    public async Task The_bom_never_reaches_the_yaml_front_end_defect_5()
    {
        var result = new PlanReader().Read([0xEF, 0xBB, 0xBF, .. "blokky: 1.0\nlog: hi\n"u8]);
        var key = result.Events.First(e => e.Kind == YamlEventKind.Scalar);

        await Assert.That(result.Ok).IsTrue();
        await Assert.That(key.Value).IsEqualTo("blokky");
        await Assert.That(key.Start).IsEqualTo(new PlanPosition(1, 1));
    }

    [Test]
    public async Task A_front_end_can_be_swapped_behind_the_interface()
    {
        var fake = new FakeFrontEnd();
        var result = new PlanReader(fake).Read("anything"u8);

        await Assert.That(Summary(result)).IsEqualTo("BK_PLAN_YAML SY-15 1:1; BK_PLAN_YAML  2:3");
    }

    private sealed class FakeFrontEnd : IYamlFrontEnd
    {
        public IReadOnlyList<YamlMarker> ScanMarkers(string text, LineMap lines) => [new(YamlMarkerKind.Directive, PlanPosition.Start)];

        public IEnumerable<YamlEvent> ReadEvents(string text, LineMap lines)
        {
            var at = new PlanPosition(2, 3);
            yield return new YamlEvent { Kind = YamlEventKind.DocumentStart, Start = at, NodeStart = at, End = at };
            yield return new YamlEvent { Kind = YamlEventKind.MappingStart, Start = at, NodeStart = at, End = at };
            yield return new YamlEvent { Kind = YamlEventKind.Error, Start = at, NodeStart = at, End = at, Value = "bad" };
            throw new InvalidOperationException("the reader reads no further than an error");
        }
    }
}
