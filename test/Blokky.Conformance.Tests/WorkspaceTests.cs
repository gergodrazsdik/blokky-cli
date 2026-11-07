using System.Diagnostics;
using System.Text.Json;
using Blokky.Conformance.Cases;
using Blokky.Conformance.Execution;

namespace Blokky.Conformance.Tests;

/// <summary>The case workspace: alias files (TK-6), log reads under the testkit's lock (TK-5) and the process marker.</summary>
public class WorkspaceTests
{
    [Test]
    [Arguments(new string[0], "")]
    [Arguments(new[] { "a" }, "a\n")]
    [Arguments(new[] { "a", "" }, "a\n\n")]
    [Arguments(new[] { "" }, "\n")]
    [Arguments(new[] { "", "x y", "" }, "\nx y\n\n")]
    public async Task Alias_files_round_trip_through_the_testkits_reading(string[] args, string file)
    {
        var text = CaseWorkspace.AliasFile(args);

        await Assert.That(text).IsEqualTo(file);
        await Assert.That(TestkitReading(text)).IsEquivalentTo(args);
    }

    [Test]
    public async Task Alias_arguments_reach_the_published_testkit_unchanged()
    {
        if (Environment.GetEnvironmentVariable("BLOKKY_TESTKIT") is not { Length: > 0 } testkit || OperatingSystem.IsWindows())
        {
            Skip.Test("BLOKKY_TESTKIT is not set (run just test-aot)");
            return;
        }

        var root = Directory.CreateTempSubdirectory("conformance-alias-").FullName;
        try
        {
            var c = CaseLoader.Parse("a/b",
                "title: t\nrules: [EM-1]\nfeatures: [exec]\nplan: |\n  blokky: 1.0\n  exec: [al]\n"
                + "programs: { al: [argv, '', 'x y', ''] }\nexpect: { status: succeeded }\n", "linux");
            using var ws = CaseWorkspace.Create(root, c.Id, keep: false);
            ws.Populate(c, testkit);
            var psi = new ProcessStartInfo(Path.Combine(ws.BinDir, "al")) { RedirectStandardOutput = true };
            using var process = Process.Start(psi)!;
            var stdout = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();
            using var json = JsonDocument.Parse(stdout);
            var argv = json.RootElement.EnumerateArray().Select(e => e.GetString() ?? "(null)").ToList();

            await Assert.That(argv).IsEquivalentTo(["", "x y", ""]);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task The_log_is_read_while_a_testkit_holds_its_lock_and_after_it_releases_it()
    {
        if (OperatingSystem.IsWindows())
        {
            Skip.Test("flock");
            return;
        }

        var root = Directory.CreateTempSubdirectory("conformance-log-").FullName;
        try
        {
            using var ws = CaseWorkspace.Create(root, "a/b", keep: false);
            await File.WriteAllTextAsync(ws.LogPath, """{"name":"blokky-testkit","argv":["exit","0"]}""" + "\n");

            // A writer as the testkit is one: an exclusive flock, the line appended in two writes while held.
            Task<List<string>> read;
            using (var writer = new FileStream(ws.LogPath, FileMode.Open, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete))
            {
                var fd = (int)writer.SafeFileHandle.DangerousGetHandle();
                await Assert.That(Interop.Flock(fd, 2)).IsEqualTo(0);
                writer.Seek(0, SeekOrigin.End);
                writer.Write("""{"name":"blokky-testkit","""u8);
                writer.Flush();
                read = Task.Run(ws.LogLines);
                await Task.Delay(150);
                var waited = !read.IsCompleted;
                writer.Write("\"argv\":[\"exit\",\"1\"]}\n"u8);
                writer.Flush();
                _ = Interop.Flock(fd, Interop.LockUn);

                await Assert.That(waited).IsTrue();
            }

            var lines = await read.WaitAsync(TimeSpan.FromSeconds(10));

            await Assert.That(lines.Count).IsEqualTo(2);
            await Assert.That(ws.ReadLog().Count).IsEqualTo(2);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task The_marker_matches_whole_environment_entries_only()
    {
        var entry = "TESTKIT_LOG=/r/a.b/testkit.log"u8.ToArray();

        await Assert.That(Survivors.HasEntry("A=1\0TESTKIT_LOG=/r/a.b/testkit.log\0B=2\0"u8, entry)).IsTrue();
        await Assert.That(Survivors.HasEntry("TESTKIT_LOG=/r/a.b/testkit.log"u8, entry)).IsTrue();
        await Assert.That(Survivors.HasEntry("TESTKIT_LOG=/r/a.b/testkit.log2\0"u8, entry)).IsFalse();
        await Assert.That(Survivors.HasEntry("XTESTKIT_LOG=/r/a.b/testkit.log\0"u8, entry)).IsFalse();
        await Assert.That(Survivors.HasEntry(""u8, entry)).IsFalse();
    }

    /// <summary>The testkit's TK-6 reading (Invocation.AliasArgs): drop one final LF, split at the rest.</summary>
    private static string[] TestkitReading(string file)
    {
        if (file.Length == 0)
        {
            return [];
        }

        return (file.EndsWith('\n') ? file[..^1] : file).Split('\n');
    }
}

/// <summary>Leftover processes (CF-12, CF-23): a fake <c>blokky</c> and a fake testkit that logs, both shell scripts.</summary>
[NotInParallel("timing")]
public class SurvivorTests
{
    private const string NotSupported =
        """{"status":"invalid","phase":"plan","errors":[{"code":"BK_PLAN_NOT_SUPPORTED","message":"no","detail":{"feature":"log"}}]}""";

    private const string Succeeded =
        """{"status":"succeeded","error":null,"nodes":[{"id":"/","kind":"exec","status":"succeeded","exit_code":0}]}""";

    [Test]
    [Arguments("(sleep 0.3; blokky-testkit exit 0)", "a process started")]
    [Arguments("(sleep 0.05; blokky-testkit exit 0)", "1 testkit invocation(s)")]
    [Arguments("sleep 30", "outlived blokky")]
    public async Task A_process_left_behind_fails_the_not_supported_check_and_is_killed(string background, string problem)
    {
        if (!OperatingSystem.IsLinux())
        {
            Skip.Test(LinuxOnly);
            return;
        }

        using var setup = new Setup();
        var file = setup.Case("features: [log]\nplan: |\n  blokky: 1.0\n  log: hi\nexpect: { status: succeeded }\n");
        var blokky = setup.FakeBlokky(NotSupported, 3, background);

        var result = await setup.Runner(blokky, "exec").RunAsync(setup.Cases, file, CancellationToken.None);

        await Assert.That(result.Outcome).IsEqualTo(Outcome.Fail);
        await Assert.That(result.Reason).Contains("not-supported check (CF-12): a process started");
        await Assert.That(result.Reason).Contains(problem);
        await Assert.That(Survivors.Find(setup.Marker)).IsEmpty();
        await Assert.That(Directory.Exists(setup.CaseRoot)).IsFalse();
        await Assert.That(setup.BlokkyLeadsItsGroup()).IsTrue();
    }

    [Test]
    public async Task A_well_behaved_fake_passes_the_check_without_waiting_out_the_settle_time()
    {
        if (!OperatingSystem.IsLinux())
        {
            Skip.Test(LinuxOnly);
            return;
        }

        using var setup = new Setup();
        var file = setup.Case("features: [log]\nplan: |\n  blokky: 1.0\n  log: hi\nexpect: { status: succeeded }\n");
        var blokky = setup.FakeBlokky(NotSupported, 3, background: null);

        var result = await setup.Runner(blokky, "exec").RunAsync(setup.Cases, file, CancellationToken.None);

        await Assert.That(result.Reason).IsNotNull();
        await Assert.That(result.Outcome).IsEqualTo(Outcome.Skip);
        await Assert.That(result.SkipKind).IsEqualTo(SkipKind.NotSupported);
    }

    [Test]
    public async Task A_late_process_that_logs_within_the_settle_time_is_counted_then_survivors_are_killed()
    {
        if (!OperatingSystem.IsLinux())
        {
            Skip.Test(LinuxOnly);
            return;
        }

        using var setup = new Setup();
        var file = setup.Case(
            "features: [exec]\nplan: |\n  blokky: 1.0\n  exec: [blokky-testkit, exit, '0']\n"
            + "expect:\n  status: succeeded\n  invocations: [{ argv: [exit, '0'] }]\n");
        var blokky = setup.FakeBlokky(Succeeded, 0, "(sleep 0.1; blokky-testkit exit 0; exec sleep 30)");

        var result = await setup.Runner(blokky, "exec").RunAsync(setup.Cases, file, CancellationToken.None);

        await Assert.That(result.Reason).IsNull();
        await Assert.That(result.Outcome).IsEqualTo(Outcome.Pass);
        await Assert.That(Survivors.Find(setup.Marker)).IsEmpty();
    }

    private const string LinuxOnly = "/proc and sh: Linux";

    /// <summary>A temporary cases dir with one case, <c>area/one</c>, and the fakes.</summary>
    [System.Runtime.Versioning.SupportedOSPlatform("linux")]
    private sealed class Setup : IDisposable
    {
        private readonly string _root = Directory.CreateTempSubdirectory("conformance-survivors-").FullName;

        public string Cases => Path.Combine(_root, "cases");

        public string CaseRoot => Path.Combine(_root, "run", "area.one");

        public string Marker => Survivors.Marker(Path.Combine(CaseRoot, "testkit.log"));

        private string GroupFile => Path.Combine(_root, "group.txt");

        public string Case(string body)
        {
            var dir = Path.Combine(Cases, "area");
            Directory.CreateDirectory(dir);
            var file = Path.Combine(dir, "one.yaml");
            File.WriteAllText(file, "title: t\nrules: [EM-1]\n" + body);
            return file;
        }

        /// <summary>Prints <paramref name="stdout"/>, records its pid and process group, leaves
        /// <paramref name="background"/> running with its stdio closed (so the runner doesn't wait for it), and exits.</summary>
        public string FakeBlokky(string stdout, int exitCode, string? background)
        {
            var leave = background is null ? "" : $"{background} >/dev/null 2>&1 </dev/null &\n";
            return Script("blokky",
                $"cat >/dev/null\nprintf '%s\\n' '{stdout}'\ncut -d' ' -f1,5 /proc/$$/stat > '{GroupFile}'\n{leave}exit {exitCode}\n");
        }

        public bool BlokkyLeadsItsGroup()
        {
            var fields = File.ReadAllText(GroupFile).Split(' ', StringSplitOptions.TrimEntries);
            return fields[0] == fields[1];
        }

        public CaseRunner Runner(string blokky, params string[] features)
        {
            // The fake testkit logs one fixed line, as `blokky-testkit exit 0` would (TK-5).
            var testkit = Script("blokky-testkit",
                "printf '%s\\n' '{\"name\":\"blokky-testkit\",\"argv\":[\"exit\",\"0\"]}' >> \"$TESTKIT_LOG\"\n");
            var set = features.ToHashSet(StringComparer.Ordinal);
            var caps = new AdapterCapabilities(set, set, ["real"], true, false);
            var bin = Path.GetDirectoryName(File.Exists("/usr/bin/sleep") ? "/usr/bin/sleep" : "/bin/sleep")!;
            var host = new HostInfo("linux", new Dictionary<string, string> { ["sh"] = bin }, new Dictionary<string, string>(), testkit,
                false);
            var settings = new RunnerSettings(Path.Combine(_root, "run"), 1, 10, false);
            return new CaseRunner(new BlokkyAdapter(blokky, new ProcessRunner(), caps), host, settings);
        }

        public void Dispose()
        {
            _ = Survivors.KillAsync(Marker, TimeSpan.FromSeconds(2)).GetAwaiter().GetResult();
            Directory.Delete(_root, recursive: true);
        }

        private string Script(string name, string body)
        {
            var dir = Path.Combine(_root, "fakes");
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, name);
            File.WriteAllText(path, "#!/bin/sh\n" + body);
            File.SetUnixFileMode(path, (UnixFileMode)Convert.ToInt32("755", 8));
            return path;
        }
    }
}
