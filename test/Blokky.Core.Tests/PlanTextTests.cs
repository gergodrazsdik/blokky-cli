using System.Text;
using Blokky.Core.Errors;
using Blokky.Core.Reading;

namespace Blokky.Core.Tests;

public class PlanTextTests
{
    [Test]
    public async Task A_utf8_bom_is_dropped_before_anything_else_sees_the_text()
    {
        var (text, error) = PlanText.Decode([0xEF, 0xBB, 0xBF, .. "a: b\n"u8]);

        await Assert.That(error).IsNull();
        await Assert.That(text).IsEqualTo("a: b\n");
    }

    [Test]
    [Arguments(new byte[] { 0xFF, 0xFE, 0x00, 0x00, 0x61, 0, 0, 0 }, "UTF-32LE")]
    [Arguments(new byte[] { 0x00, 0x00, 0xFE, 0xFF, 0, 0, 0, 0x61 }, "UTF-32BE")]
    [Arguments(new byte[] { 0xFF, 0xFE, 0x61, 0x00 }, "UTF-16LE")]
    [Arguments(new byte[] { 0xFE, 0xFF, 0x00, 0x61 }, "UTF-16BE")]
    public async Task Utf16_and_utf32_boms_are_one_whole_text_error(byte[] raw, string encoding)
    {
        var (text, error) = PlanText.Decode(raw);

        await Assert.That(text).IsNull();
        await Assert.That(error!.Code).IsEqualTo(ErrorCodes.PlanEncoding);
        await Assert.That(error.Pos).IsEqualTo("1:1");
        await Assert.That(Rule(error)).IsEqualTo("SY-6");
        await Assert.That(error.Message).Contains(encoding);
        await Assert.That(error.Message).Contains("-Encoding utf8");
    }

    [Test]
    [Arguments(new byte[] { 0x61, 0x0A, 0x62, 0xFF }, 3, "2:2")]
    [Arguments(new byte[] { 0x61, 0xC0, 0xAF }, 1, "1:2")]
    [Arguments(new byte[] { 0x61, 0xED, 0xA0, 0x80 }, 1, "1:2")]
    [Arguments(new byte[] { 0xC3, 0xA9, 0x62, 0xE2, 0x82 }, 3, "1:3")]
    [Arguments(new byte[] { 0xEF, 0xBB, 0xBF, 0x61, 0xFF }, 4, "1:2")]
    [Arguments(new byte[] { 0x61, 0x0D, 0x62, 0x80 }, 3, "2:2")]
    public async Task Invalid_utf8_is_one_error_at_the_first_bad_byte(byte[] raw, int offset, string pos)
    {
        var (_, error) = PlanText.Decode(raw);

        await Assert.That(error!.Code).IsEqualTo(ErrorCodes.PlanEncoding);
        await Assert.That(Rule(error)).IsEqualTo("SY-7");
        await Assert.That(error.Detail!["offset"]!.GetValue<int>()).IsEqualTo(offset);
        await Assert.That(error.Pos).IsEqualTo(pos);
    }

    [Test]
    public async Task The_size_cap_counts_raw_bytes()
    {
        var atCap = new byte[PlanText.MaxBytes];
        atCap.AsSpan().Fill((byte)'#');
        var over = new byte[PlanText.MaxBytes + 1];

        await Assert.That(PlanText.Decode(atCap).Error).IsNull();
        var error = PlanText.Decode(over).Error!;
        await Assert.That(error.Code).IsEqualTo(ErrorCodes.PlanTooLarge);
        await Assert.That(error.Pos).IsEqualTo("1:1");
        await Assert.That(Rule(error)).IsEqualTo("SY-9");
    }

    [Test]
    public async Task Non_ascii_text_decodes_unchanged()
    {
        var text = "log: 'Grüße\u00a0世界 \U0001F600'\n";

        await Assert.That(PlanText.Decode(Encoding.UTF8.GetBytes(text)).Text).IsEqualTo(text);
    }

    internal static string? Rule(BlokkyError error) => error.Detail?["rule"]?.GetValue<string>();
}
