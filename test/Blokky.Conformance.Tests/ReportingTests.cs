using Blokky.Conformance.Cases;
using Blokky.Conformance.Execution;
using Blokky.Conformance.Reporting;

namespace Blokky.Conformance.Tests;

public class ReportingTests
{
    [Test]
    public async Task The_lock_file_is_key_value_lines_with_comments()
    {
        var pin = LockFile.Parse("# pin\nrepository: o/r\ntag: v1.0.0-draft.5\ncommit: 8d01629\n");

        await Assert.That(pin).IsEqualTo(new LockFile("o/r", "v1.0.0-draft.5", "8d01629"));
        await Assert.That(() => LockFile.Parse("branch: main\n")).Throws<FormatException>();
    }

    [Test]
    public async Task The_pass_rate_counts_not_supported_skips_but_not_requirement_or_platform_skips()
    {
        CaseResult R(Outcome o, SkipKind? k = null) => new("a/b", o, null, k, [], null, TimeSpan.Zero);
        var totals = Totals.Of([
            R(Outcome.Pass), R(Outcome.Pass), R(Outcome.Fail),
            R(Outcome.Skip, SkipKind.NotSupported), R(Outcome.Skip, SkipKind.Requirement), R(Outcome.Skip, SkipKind.Platform),
        ]);

        await Assert.That(totals.Counted).IsEqualTo(4);
        await Assert.That(totals.PassRate).IsEqualTo(50.0);
        await Assert.That(totals.Skip).IsEqualTo(3);
    }
}
