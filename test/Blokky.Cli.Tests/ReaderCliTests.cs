using System.Text.Json;
using Blokky.Core.Reading;

namespace Blokky.Cli.Tests;

/// <summary><c>validate</c> and <c>run</c> read the plan first: reader errors are the pre-run envelope (exit 3, phase plan).</summary>
public class ReaderCliTests
{
    private static string[] Summary(JsonElement root) =>
    [
        .. root.GetProperty("errors").EnumerateArray().Select(e =>
            $"{e.GetProperty("code").GetString()} {e.GetProperty("pos").GetString()} {Rule(e)}".TrimEnd()),
    ];

    private static string? Rule(JsonElement error) =>
        error.TryGetProperty("detail", out var detail)
            ? (detail.TryGetProperty("rule", out var rule) ? rule : detail.TryGetProperty("feature", out var feature) ? feature : default)
                is { ValueKind: JsonValueKind.String } value ? value.GetString() : null
            : null;

    [Test]
    [Arguments("run")]
    [Arguments("validate")]
    public async Task Yaml_errors_are_all_reported_with_positions_and_rules_and_the_plan_is_still_checked(string command)
    {
        var result = CliHarness.Run("blokky: 1.0\nname: !custom x\nlog: hi\nlog: again\n", command, "-");
        using var json = result.Json();

        await Assert.That(result.ExitCode).IsEqualTo(3);
        await Assert.That(json.RootElement.GetProperty("status").GetString()).IsEqualTo("invalid");
        await Assert.That(json.RootElement.GetProperty("phase").GetString()).IsEqualTo("plan");
        await Assert.That(string.Join("; ", Summary(json.RootElement)))
            .IsEqualTo("BK_PLAN_YAML 2:7 SY-15; BK_PLAN_DUPLICATE_KEY 4:1 SY-18; BK_PLAN_NOT_SUPPORTED 3:1 log");
        await Assert.That(result.Stderr).StartsWith("blokky: error BK_PLAN_YAML at 2:7: ");
        await Assert.That(result.Stderr.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length).IsEqualTo(3);
    }

    [Test]
    public async Task An_encoding_error_carries_its_byte_offset()
    {
        var result = CliHarness.Run([0xEF, 0xBB, 0xBF, .. "blokky: 1.0\nlog: abc"u8, 0xFF, 0x0A], "run", "-");
        using var json = result.Json();
        var error = json.RootElement.GetProperty("errors")[0];

        await Assert.That(result.ExitCode).IsEqualTo(3);
        await Assert.That(error.GetProperty("code").GetString()).IsEqualTo("BK_PLAN_ENCODING");
        await Assert.That(error.GetProperty("pos").GetString()).IsEqualTo("2:9");
        await Assert.That(error.GetProperty("detail").GetProperty("offset").GetInt32()).IsEqualTo(23);
        await Assert.That(error.GetProperty("detail").GetProperty("rule").GetString()).IsEqualTo("SY-7");
    }

    [Test]
    public async Task A_utf16_plan_is_one_error_at_1_1()
    {
        var result = CliHarness.Run([0xFF, 0xFE, 0x61, 0x00, 0x0A, 0x00], "validate", "-");
        using var json = result.Json();

        await Assert.That(string.Join("; ", Summary(json.RootElement))).IsEqualTo("BK_PLAN_ENCODING 1:1 SY-6");
    }

    [Test]
    public async Task Rejected_characters_are_json_escaped_on_stdout_and_named_on_stderr()
    {
        var result = CliHarness.Run("blokky: 1.0\n# \u202Eadmin\nlog: 'a\u001bb'\n", "run", "-");
        using var json = result.Json();
        var codepoints = json.RootElement.GetProperty("errors").EnumerateArray()
            .Select(e => e.GetProperty("detail").GetProperty("codepoint").GetString());

        await Assert.That(string.Join("; ", Summary(json.RootElement)))
            .IsEqualTo("BK_PLAN_CHARACTER 2:3 SY-12; BK_PLAN_CHARACTER 3:8 SY-12");
        await Assert.That(string.Join(" ", codepoints)).IsEqualTo("U+202E U+001B");
        await Assert.That(result.Stderr).Contains("RIGHT-TO-LEFT OVERRIDE");
        await Assert.That(result.Stdout).DoesNotContain("\u001b");
    }

    [Test]
    public async Task A_list_root_is_a_value_error()
    {
        var result = CliHarness.Run("- blokky: 1.0\n- log: hi\n", "run", "-");
        using var json = result.Json();

        await Assert.That(string.Join("; ", Summary(json.RootElement))).IsEqualTo("BK_PLAN_VALUE 1:1 SY-32");
    }

    [Test]
    [Arguments("run")]
    [Arguments("validate")]
    public async Task A_plan_that_reads_cleanly_still_stops_at_the_feature_gate(string command)
    {
        var result = CliHarness.Run("\uFEFF---\r\nblokky: 1.0\r\nlog: 'a\tb' # note\r\n", command, "-");
        using var json = result.Json();
        var error = json.RootElement.GetProperty("errors")[0];

        await Assert.That(result.ExitCode).IsEqualTo(3);
        await Assert.That(error.GetProperty("code").GetString()).IsEqualTo("BK_PLAN_NOT_SUPPORTED");
        await Assert.That(error.GetProperty("detail").GetProperty("feature").GetString()).IsEqualTo("log");
    }

    [Test]
    [Arguments("run")]
    [Arguments("validate")]
    public async Task An_endless_stdin_is_read_one_byte_past_the_size_cap(string command)
    {
        var stdin = new EndlessStream();
        using var stdout = new MemoryStream();
        using var stderr = new MemoryStream();

        var code = await Task.Run(() => CliApp.Run([command, "-"], stdin, stdout, stderr)).WaitAsync(TimeSpan.FromSeconds(60));
        using var json = JsonDocument.Parse(stdout.ToArray());

        await Assert.That(code).IsEqualTo(3);
        await Assert.That(string.Join("; ", Summary(json.RootElement))).IsEqualTo("BK_PLAN_TOO_LARGE 1:1 SY-9");
        await Assert.That(stdin.Position).IsEqualTo(PlanText.MaxBytes + 1L);
    }

    [Test]
    public async Task A_plan_file_over_the_cap_is_the_same_error()
    {
        var path = Path.GetTempFileName();
        try
        {
            await File.WriteAllBytesAsync(path, new byte[PlanText.MaxBytes + 10]);
            var result = CliHarness.Run("", "validate", path);
            using var json = result.Json();

            await Assert.That(result.ExitCode).IsEqualTo(3);
            await Assert.That(string.Join("; ", Summary(json.RootElement))).IsEqualTo("BK_PLAN_TOO_LARGE 1:1 SY-9");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public async Task An_endless_plan_file_is_read_one_byte_past_the_size_cap()
    {
        if (!File.Exists("/dev/zero"))
        {
            Skip.Test("needs /dev/zero");
        }

        var result = await Task.Run(() => CliHarness.Run("", "run", "/dev/zero")).WaitAsync(TimeSpan.FromSeconds(60));
        using var json = result.Json();

        await Assert.That(string.Join("; ", Summary(json.RootElement))).IsEqualTo("BK_PLAN_TOO_LARGE 1:1 SY-9");
    }

    /// <summary>A read-only stream of <c>#</c> that never ends.</summary>
    private sealed class EndlessStream : Stream
    {
        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position { get; set; }

        public override int Read(byte[] buffer, int offset, int count)
        {
            buffer.AsSpan(offset, count).Fill((byte)'#');
            Position += count;
            return count;
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
