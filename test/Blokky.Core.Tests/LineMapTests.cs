using Blokky.Core.Reading;

namespace Blokky.Core.Tests;

public class LineMapTests
{
    [Test]
    [Arguments("a\nb", 2, 2, 1)]
    [Arguments("a\r\nb", 3, 2, 1)]
    [Arguments("a\rb", 2, 2, 1)]
    [Arguments("ab\n", 1, 1, 2)]
    [Arguments("ab\n", 3, 2, 1)]
    [Arguments("a\u2028b", 2, 1, 3)]
    [Arguments("a\u0085b", 2, 1, 3)]
    public async Task Lines_end_at_lf_crlf_and_lone_cr_only(string text, int index, int line, int column)
    {
        await Assert.That(new LineMap(text).At(index)).IsEqualTo(new PlanPosition(line, column));
    }

    [Test]
    public async Task Columns_count_code_points()
    {
        var text = "log: [\U0001F600, é, *a]";
        var map = new LineMap(text);

        await Assert.That(map.At(text.IndexOf('é', StringComparison.Ordinal))).IsEqualTo(new PlanPosition(1, 10));
        await Assert.That(map.At(text.IndexOf('*', StringComparison.Ordinal))).IsEqualTo(new PlanPosition(1, 13));
    }

    [Test]
    public async Task Indexes_are_clamped_and_positions_format_as_line_colon_column()
    {
        var map = new LineMap("ab\ncd");

        await Assert.That(map.At(-5)).IsEqualTo(PlanPosition.Start);
        await Assert.That(map.At(99)).IsEqualTo(new PlanPosition(2, 3));
        await Assert.That(map.LineCount).IsEqualTo(2);
        await Assert.That(map.LineStart(2)).IsEqualTo(3);
        await Assert.That(new PlanPosition(12, 7).ToString()).IsEqualTo("12:7");
        await Assert.That(new LineMap("").At(0)).IsEqualTo(PlanPosition.Start);
    }
}
