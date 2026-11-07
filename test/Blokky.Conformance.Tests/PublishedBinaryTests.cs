using Blokky.Conformance.Execution;

namespace Blokky.Conformance.Tests;

/// <summary>Skips unless <c>BLOKKY_BIN</c> names the published AOT binary (<c>just test-aot</c>, CI).</summary>
internal sealed class RequiresPublishedBinaryAttribute() : SkipAttribute("BLOKKY_BIN is not set (run just test-aot)")
{
    public override Task<bool> ShouldSkip(TestRegisteredContext context) =>
        Task.FromResult(string.IsNullOrEmpty(Environment.GetEnvironmentVariable("BLOKKY_BIN")));
}

/// <summary>The adapter against the real binary: the stub passes the not-supported check (CF-12).</summary>
public class PublishedBinaryTests
{
    [Test]
    [RequiresPublishedBinary]
    public async Task The_stub_passes_the_not_supported_check()
    {
        var dir = Directory.CreateTempSubdirectory("conformance-adapter-").FullName;
        try
        {
            var adapter = new BlokkyAdapter(Environment.GetEnvironmentVariable("BLOKKY_BIN")!, new ProcessRunner());
            var env = new Dictionary<string, string> { ["HOME"] = dir, ["PATH"] = "/usr/bin:/bin" };
            var plan = "blokky: 1.0\ndo:\n  - exec: [blokky-testkit, exit, '0']\n"u8.ToArray();

            var outcome = await adapter.RunAsync(new RunRequest(plan, [], env, dir, TimeSpan.FromSeconds(30), []), CancellationToken.None);

            await Assert.That(CaseRunner.NotSupportedProblem(outcome, 0, ["do", "exec"]).Problem).IsNull();
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
