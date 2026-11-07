using Blokky.Core.Errors;
using Blokky.Core.Reading;

namespace Blokky.Core.Tests;

public class CharacterLintTests
{
    private static IReadOnlyList<BlokkyError> Lint(string text) => CharacterLint.Check(text);

    [Test]
    [Arguments(0x00, "NULL")]
    [Arguments(0x07, "BELL")]
    [Arguments(0x0B, "LINE TABULATION")]
    [Arguments(0x1B, "ESCAPE")]
    [Arguments(0x7F, "DELETE")]
    [Arguments(0x85, "NEXT LINE")]
    [Arguments(0x9B, "CONTROL SEQUENCE INTRODUCER")]
    [Arguments(0x061C, "ARABIC LETTER MARK")]
    [Arguments(0x200B, "ZERO WIDTH SPACE")]
    [Arguments(0x200F, "RIGHT-TO-LEFT MARK")]
    [Arguments(0x202E, "RIGHT-TO-LEFT OVERRIDE")]
    [Arguments(0x2060, "WORD JOINER")]
    [Arguments(0x2069, "POP DIRECTIONAL ISOLATE")]
    [Arguments(0x2028, "LINE SEPARATOR")]
    [Arguments(0x2029, "PARAGRAPH SEPARATOR")]
    [Arguments(0xFEFF, "ZERO WIDTH NO-BREAK SPACE")]
    public async Task Rejected_characters_are_reported_with_codepoint_name_and_position(int codepoint, string name)
    {
        var errors = Lint($"a: 1\n# x{(char)codepoint}\n");

        await Assert.That(errors.Count).IsEqualTo(1);
        await Assert.That(errors[0].Code).IsEqualTo(ErrorCodes.PlanCharacter);
        await Assert.That(errors[0].Pos).IsEqualTo("2:4");
        await Assert.That(errors[0].Detail!["codepoint"]!.GetValue<string>()).IsEqualTo($"U+{codepoint:X4}");
        await Assert.That(PlanTextTests.Rule(errors[0])).IsEqualTo("SY-12");
        await Assert.That(errors[0].Message).Contains(name);
    }

    [Test]
    public async Task Tab_lf_crlf_and_ordinary_unicode_are_allowed()
    {
        await Assert.That(Lint("a:\t'b'\r\n# \u00a0é世\U0001F600\u200A\u202F\n").Count).IsEqualTo(0);
    }

    [Test]
    public async Task A_lone_cr_is_rejected_ends_its_line_and_every_error_is_collected()
    {
        var errors = Lint("a: 'x\ry'\nb: '\u0007'\u202E\n");

        await Assert.That(string.Join(" ", errors.Select(e => e.Pos))).IsEqualTo("1:6 3:5 3:7");
        await Assert.That(errors[0].Detail!["codepoint"]!.GetValue<string>()).IsEqualTo("U+000D");
    }

    [Test]
    public async Task Positions_count_code_points_and_every_line_break()
    {
        var errors = Lint("\U0001F600\u0001\r\n\u0001\r\u0001\n\t\u202E");

        await Assert.That(string.Join(" ", errors.Select(e => e.Pos))).IsEqualTo("1:2 2:1 2:2 3:1 4:2");
    }

    [Test]
    public async Task A_cr_at_the_very_end_is_a_lone_cr()
    {
        await Assert.That(Lint("a: b\r").Single().Pos).IsEqualTo("1:5");
    }
}
