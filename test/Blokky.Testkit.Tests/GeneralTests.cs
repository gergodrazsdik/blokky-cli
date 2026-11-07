using System.Text.Json;

namespace Blokky.Testkit.Tests;

/// <summary>testkit-interface.md §1: invocation, values, output, log, aliases, common options, signals, exit codes.</summary>
public class GeneralTests
{
    // TK-1, TK-4, §1.1: usage errors exit 125 with one blokky-testkit: line on stderr and nothing on stdout.
    [Test]
    [Arguments("")]
    [Arguments("bogus")]
    [Arguments("print --nope")]
    [Arguments("print --out")]
    [Arguments("exit")]
    [Arguments("sleep 1s --exit 1 --exit 2")]
    public async Task TK1_TK4_usage_errors_exit_125_with_one_stderr_line(string argLine)
    {
        var args = argLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var result = await Tk.RunAsync(args);

        await Assert.That(result.ExitCode).IsEqualTo(125);
        await Assert.That(result.StdoutBytes).IsEmpty();
        await Assert.That(result.Stderr).StartsWith("blokky-testkit: ");
        await Assert.That(result.Stderr.TrimEnd('\n')).DoesNotContain('\n');
    }

    [Test]
    public async Task TK1_an_option_value_is_the_next_argument_even_if_it_starts_with_dashes()
    {
        var result = await Tk.RunAsync("print", "--out", "--err");

        await Assert.That(result.Stdout).IsEqualTo("--err\n");
    }

    [Test]
    public async Task TK1_options_may_come_in_any_order_around_positionals()
    {
        var result = await Tk.RunAsync("sleep", "--exit", "4", "0s");

        await Assert.That(result.ExitCode).IsEqualTo(4);
    }

    [Test]
    public async Task TK1_double_dash_ends_the_options()
    {
        var options = new TkOptions();
        options.Env["--all"] = "x";
        var result = await Tk.RunAsync(options, "env", "--", "--all");

        await Assert.That(result.Stdout).IsEqualTo("{\"--all\":\"x\"}\n");
    }

    // TK-2: durations follow the plan grammar plus 0s.
    [Test]
    [Arguments("0s", 0)]
    [Arguments("1ms", 0)]
    [Arguments("1m30s500ms", -1)]
    [Arguments("0ms", 125)]
    [Arguments("0m0s", 125)]
    [Arguments("1.5s", 125)]
    [Arguments("30", 125)]
    [Arguments("01s", 125)]
    [Arguments("30s1m", 125)]
    [Arguments("1s1s", 125)]
    [Arguments("169h", 125)]
    [Arguments("-1s", 125)]
    public async Task TK2_durations_use_the_plan_grammar(string duration, int expected)
    {
        // -1: valid but long; checked with print's --sleep validation only (a usage error comes before any sleep).
        var args = expected == -1 ? new[] { "print", "--sleep", duration, "--out-hex", "zz" } : new[] { "sleep", duration };
        var result = await Tk.RunAsync(args);

        // The --out-hex error proves --sleep's duration was accepted (operations are validated before they run).
        await Assert.That(result.ExitCode).IsEqualTo(expected == -1 ? 125 : expected);
        if (expected == -1)
        {
            await Assert.That(result.Stderr).Contains("--out-hex");
        }
    }

    [Test]
    [NotInParallel("timing")]
    public async Task TK2_durations_are_multiplied_by_TESTKIT_TIME_SCALE()
    {
        var options = new TkOptions();
        options.Env["TESTKIT_TIME_SCALE"] = "0.1";
        var result = await Tk.RunAsync(options, "sleep", "3s");

        await Assert.That(result.ExitCode).IsEqualTo(0);
        await Assert.That(result.Elapsed).IsLessThan(TimeSpan.FromSeconds(2));
        await Assert.That(result.Elapsed).IsGreaterThanOrEqualTo(TimeSpan.FromMilliseconds(250));
    }

    [Test]
    [Arguments("0")]
    [Arguments("1.5")]
    [Arguments("-0.5")]
    [Arguments("1/10")]
    public async Task TK2_a_time_scale_outside_0_to_1_is_a_usage_error(string scale)
    {
        var options = new TkOptions();
        options.Env["TESTKIT_TIME_SCALE"] = scale;
        var result = await Tk.RunAsync(options, "sleep", "1ms");

        await Assert.That(result.ExitCode).IsEqualTo(125);
    }

    [Test]
    [Arguments("100", 100)]
    [Arguments("100B", 100)]
    [Arguments("2KiB", 2048)]
    [Arguments("1MiB", 1048576)]
    public async Task TK2_sizes_use_the_size_grammar_or_bare_bytes(string size, int bytes)
    {
        var result = await Tk.RunAsync("print", "--out-fill", size);

        await Assert.That(result.StdoutBytes.Length).IsEqualTo(bytes);
    }

    [Test]
    [Arguments("1MB")]
    [Arguments("1.5KiB")]
    [Arguments("KiB")]
    [Arguments("1 KiB")]
    public async Task TK2_malformed_sizes_are_usage_errors(string size)
    {
        var result = await Tk.RunAsync("print", "--out-fill", size);

        await Assert.That(result.ExitCode).IsEqualTo(125);
    }

    // TK-3: hex arguments are pairs of hex digits and produce exact bytes.
    [Test]
    public async Task TK3_hex_produces_any_bytes()
    {
        var result = await Tk.RunAsync("print", "--out-hex", "ff00FEef0d");

        await Assert.That(result.StdoutBytes).IsEquivalentTo(new byte[] { 0xff, 0x00, 0xfe, 0xef, 0x0d });
    }

    [Test]
    [Arguments("f")]
    [Arguments("zz")]
    [Arguments("0x00")]
    public async Task TK3_malformed_hex_is_a_usage_error(string hex)
    {
        var result = await Tk.RunAsync("print", "--out-hex", hex);

        await Assert.That(result.ExitCode).IsEqualTo(125);
    }

    [Test]
    public async Task TK3_text_is_written_as_utf8()
    {
        var result = await Tk.RunAsync("print", "--out", "héllo ✓");

        await Assert.That(result.Stdout).IsEqualTo("héllo ✓\n");
    }

    // TK-4: every write is flushed at once, so a merged pipe sees the write order.
    [Test]
    public async Task TK4_a_merged_pipe_sees_writes_in_order()
    {
        var result = await Tk.ShellAsync("\"$TK\" print --out a --err b --out c --err-hex 64 --out-hex 65 2>&1");

        await Assert.That(result.Stdout).IsEqualTo("a\nb\nc\nde");
    }

    // TK-5: the invocation log.
    [Test]
    public async Task TK5_each_invocation_appends_one_json_line_before_acting()
    {
        using var dir = new TempDir();
        var options = dir.Options();
        options.Env["TESTKIT_LOG"] = dir.File("log.jsonl");
        var result = await Tk.RunAsync(options, "exit", "3");
        await Tk.RunAsync(options, "bogus", "x");

        var lines = File.ReadAllLines(dir.File("log.jsonl"));
        await Assert.That(result.ExitCode).IsEqualTo(3);
        await Assert.That(lines.Length).IsEqualTo(2);
        using var first = JsonDocument.Parse(lines[0]);
        var root = first.RootElement;
        await Assert.That(root.GetProperty("name").GetString()).IsEqualTo("blokky-testkit");
        var argv = root.GetProperty("argv").EnumerateArray().Select(e => e.GetString()!).ToArray();
        await Assert.That(argv).IsEquivalentTo(new[] { "exit", "3" });
        await Assert.That(root.GetProperty("pid").GetInt32()).IsGreaterThan(0);
        await Assert.That(root.GetProperty("ppid").GetInt32()).IsEqualTo(Environment.ProcessId);
        await Assert.That(root.GetProperty("cwd").GetString()).IsEqualTo(dir.Path);
        await Assert.That(lines[0]).DoesNotContain(" ");
        using var second = JsonDocument.Parse(lines[1]);
        await Assert.That(second.RootElement.GetProperty("argv")[0].GetString()).IsEqualTo("bogus");
    }

    [Test]
    public async Task TK5_concurrent_appends_never_interleave()
    {
        using var dir = new TempDir();
        var options = dir.Options();
        options.Env["TESTKIT_LOG"] = dir.File("log.jsonl");
        var payload = new string('p', 3000);
        await Task.WhenAll(Enumerable.Range(0, 24)
            .Select(i => Tk.RunAsync(options, "argv", payload, i.ToString(System.Globalization.CultureInfo.InvariantCulture))));

        var lines = File.ReadAllLines(dir.File("log.jsonl"));
        await Assert.That(lines.Length).IsEqualTo(24);
        var indexes = new HashSet<string>();
        foreach (var line in lines)
        {
            using var json = JsonDocument.Parse(line);
            var argv = json.RootElement.GetProperty("argv");
            await Assert.That(argv[1].GetString()).IsEqualTo(payload);
            indexes.Add(argv[2].GetString()!);
        }

        await Assert.That(indexes.Count).IsEqualTo(24);
    }

    [Test]
    public async Task TK5_a_log_that_cant_be_written_exits_124_before_acting()
    {
        using var dir = new TempDir();
        var options = dir.Options();
        options.Env["TESTKIT_LOG"] = dir.File("missing-dir/log.jsonl");
        var result = await Tk.RunAsync(options, "print", "--out", "acted");

        await Assert.That(result.ExitCode).IsEqualTo(124);
        await Assert.That(result.StdoutBytes).IsEmpty();
    }

    // TK-6: aliases.
    [Test]
    public async Task TK6_a_symlinked_alias_reads_its_args_from_the_resolved_directory()
    {
        using var dir = new TempDir();
        var alias = $"tkalias{Guid.NewGuid():N}";
        var argsFile = Path.Combine(Path.GetDirectoryName(Tk.Path)!, alias + ".testkit-args");
        File.WriteAllText(argsFile, "argv\nfrom alias\n\n");
        try
        {
            File.CreateSymbolicLink(dir.File(alias), Tk.Path);
            var options = new TkOptions { Program = dir.File(alias), WorkingDirectory = dir.Path };
            options.Env["TESTKIT_LOG"] = dir.File("log.jsonl");
            var result = await Tk.RunAsync(options, "own");

            await Assert.That(result.Stdout).IsEqualTo("[\"from alias\",\"\",\"own\"]\n");
            using var log = JsonDocument.Parse(File.ReadAllText(dir.File("log.jsonl")));
            await Assert.That(log.RootElement.GetProperty("name").GetString()).IsEqualTo(alias);
            await Assert.That(log.RootElement.GetProperty("argv").GetArrayLength()).IsEqualTo(4);
        }
        finally
        {
            File.Delete(argsFile);
        }
    }

    [Test]
    public async Task TK6_a_copied_alias_reads_its_args_from_its_own_directory()
    {
        if (!Tk.IsSelfContained)
        {
            Skip.Test("needs the self-contained AOT binary (just test-aot)");
        }

        using var dir = new TempDir();
        File.Copy(Tk.Path, dir.File("git"));
        File.WriteAllText(dir.File("git.testkit-args"), "print\n--out\ngit version 2.0\n");
        TkResult? result = null;
        for (var attempt = 0; result is null; attempt++)
        {
            try
            {
                result = await Tk.RunAsync(new TkOptions { Program = dir.File("git") }, "--exit", "2");
            }
            catch (System.ComponentModel.Win32Exception) when (attempt < 50)
            {
                // ETXTBSY: a process forked concurrently by this test host still holds the copy's write descriptor until it execs.
                await Task.Delay(50);
            }
        }


        await Assert.That(result.Stdout).IsEqualTo("git version 2.0\n");
        await Assert.That(result.ExitCode).IsEqualTo(2);
    }

    [Test]
    public async Task TK6_an_alias_without_its_args_file_exits_125()
    {
        using var dir = new TempDir();
        File.CreateSymbolicLink(dir.File($"noargs{Guid.NewGuid():N}"), Tk.Path);
        var program = Directory.GetFiles(dir.Path).Single();
        var result = await Tk.RunAsync(new TkOptions { Program = program }, "exit", "0");

        await Assert.That(result.ExitCode).IsEqualTo(125);
    }

    // TK-7: common options.
    [Test]
    public async Task TK7_pid_file_holds_the_pid_and_a_newline()
    {
        using var dir = new TempDir();
        var options = dir.Options();
        options.Env["TESTKIT_LOG"] = dir.File("log.jsonl");
        await Tk.RunAsync(options, "exit", "0", "--pid-file", "pid.txt");

        using var log = JsonDocument.Parse(File.ReadAllText(dir.File("log.jsonl")));
        await Assert.That(File.ReadAllText(dir.File("pid.txt"))).IsEqualTo($"{log.RootElement.GetProperty("pid").GetInt32()}\n");
    }

    [Test]
    public async Task TK7_cwd_check_passes_through_a_symlink_and_fails_elsewhere()
    {
        using var dir = new TempDir();
        Directory.CreateDirectory(dir.File("real"));
        File.CreateSymbolicLink(dir.File("link"), dir.File("real"));
        var options = new TkOptions { WorkingDirectory = dir.File("real") };

        var match = await Tk.RunAsync(options, "exit", "0", "--cwd-check", dir.File("link"));
        var mismatch = await Tk.RunAsync(options, "exit", "0", "--cwd-check", dir.Path);

        await Assert.That(match.ExitCode).IsEqualTo(0);
        await Assert.That(mismatch.ExitCode).IsEqualTo(125);
    }

    [Test]
    public async Task TK7_TK12_argv_doesnt_interpret_common_options()
    {
        var result = await Tk.RunAsync("argv", "--pid-file", "x", "--cwd-check", "/nowhere");

        await Assert.That(result.Stdout).IsEqualTo("[\"--pid-file\",\"x\",\"--cwd-check\",\"/nowhere\"]\n");
    }
}
