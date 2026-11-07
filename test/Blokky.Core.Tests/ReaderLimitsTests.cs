using System.Text;
using Blokky.Core.Errors;
using Blokky.Core.Reading;

namespace Blokky.Core.Tests;

/// <summary>
/// The reader's running time and memory on large plans, up to the 4 MiB cap (SY-9): no part of it may be quadratic. The
/// bounds are generous (debug builds, parallel tests); the timeouts make a regression fail rather than hang.
/// </summary>
public class ReaderLimitsTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(60);

    private static byte[] Utf8(string text) => Encoding.UTF8.GetBytes(text);

    [Test]
    public async Task Positions_on_a_4_MiB_line_cost_no_more_than_a_search()
    {
        var text = "x: " + new string('a', PlanText.MaxBytes - 4) + "\U0001F600";
        var map = new LineMap(text);

        var (columns, elapsed, _) = await Bounded.RunAsync(() =>
        {
            var sum = 0L;
            for (var i = 0; i < 200_000; i++)
            {
                sum += map.At(text.Length - 3 - (i % 1000)).Column;
            }

            return sum;
        }, Timeout);

        await Assert.That(columns).IsGreaterThan(0);
        await Assert.That(map.At(text.Length)).IsEqualTo(new PlanPosition(1, PlanText.MaxBytes + 1));
        await Assert.That(elapsed).IsLessThan(TimeSpan.FromSeconds(5));
    }

    [Test]
    public async Task A_4_MiB_one_line_flow_mapping_reads_in_linear_time()
    {
        var plan = new StringBuilder("{blokky: 1.0, log: x, vars: {");
        for (var i = 0; plan.Length < PlanText.MaxBytes - 32; i++)
        {
            plan.Append('K').Append(i).Append(": v, ");
        }

        plan.Append("Z: v}}\n");
        var bytes = Utf8(plan.ToString());

        var (result, elapsed, _) = await Bounded.RunAsync(() => new PlanReader().Read(bytes), Timeout);

        await Assert.That(result.Ok).IsTrue();
        await Assert.That(elapsed).IsLessThan(TimeSpan.FromSeconds(20));
    }

    [Test]
    public async Task A_MiB_of_rejected_characters_on_one_line_is_linear_and_small()
    {
        const int Count = 1024 * 1024;
        var bytes = Utf8("x: '" + new string('\u0001', Count) + "'\n");

        var (result, elapsed, allocated) = await Bounded.RunAsync(() => new PlanReader().Read(bytes), Timeout);

        await Assert.That(result.Errors.Count).IsEqualTo(Count);
        await Assert.That(result.Errors[^1].Pos).IsEqualTo($"1:{Count + 4}");
        await Assert.That(result.Errors[^1].Detail!["codepoint"]!.GetValue<string>()).IsEqualTo("U+0001");
        await Assert.That(result.Errors[^1].Message).IsSameReferenceAs(result.Errors[0].Message);
        await Assert.That(elapsed).IsLessThan(TimeSpan.FromSeconds(10));
        await Assert.That(allocated / Count).IsLessThan(400);
    }

    [Test]
    public async Task A_4_MiB_run_of_open_brackets_stops_at_the_depth_cap()
    {
        var bytes = Utf8(new string('[', PlanText.MaxBytes));

        var (result, elapsed, _) = await Bounded.RunAsync(() => new PlanReader().Read(bytes), Timeout);

        await Assert.That(result.Errors.Single().Code).IsEqualTo(ErrorCodes.PlanTooLarge);
        await Assert.That(result.Errors.Single().Pos).IsEqualTo("1:129");
        await Assert.That(elapsed).IsLessThan(TimeSpan.FromSeconds(5));
    }

    [Test]
    public async Task Deep_flow_collections_on_many_lines_stop_at_the_depth_cap()
    {
        var bytes = Utf8("a:\n" + string.Concat(Enumerable.Repeat(" [\n", 1_000_000)));

        var (result, elapsed, _) = await Bounded.RunAsync(() => new PlanReader().Read(bytes), Timeout);

        await Assert.That(result.Errors.Single().Code).IsEqualTo(ErrorCodes.PlanTooLarge);
        await Assert.That(result.Errors.Single().Pos).IsEqualTo("129:2");
        await Assert.That(elapsed).IsLessThan(TimeSpan.FromSeconds(5));
    }
}
