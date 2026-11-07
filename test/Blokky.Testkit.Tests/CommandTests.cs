using System.Globalization;
using System.Text.Json;

namespace Blokky.Testkit.Tests;

/// <summary>testkit-interface.md §2: the commands that don't involve other processes or signals.</summary>
public class CommandTests
{
    // TK-9: exit.
    [Test]
    [Arguments("0", 0)]
    [Arguments("3", 3)]
    [Arguments("255", 255)]
    [Arguments("256", 125)]
    [Arguments("-1", 125)]
    [Arguments("x", 125)]
    [Arguments("", 125)]
    public async Task TK9_exit_exits_with_the_code(string code, int expected)
    {
        var result = await Tk.RunAsync("exit", code);

        await Assert.That(result.ExitCode).IsEqualTo(expected);
    }

    // TK-10: print.
    [Test]
    public async Task TK10_operations_run_in_order_with_lf_by_default()
    {
        var result = await Tk.RunAsync("print", "--out", "a", "--err", "b", "--out", "", "--out-hex", "0d");

        await Assert.That(result.Stdout).IsEqualTo("a\n\n\r");
        await Assert.That(result.Stderr).IsEqualTo("b\n");
    }

    [Test]
    [Arguments("crlf", "x\r\ny\r\n")]
    [Arguments("none", "xy")]
    [Arguments("lf", "x\ny\n")]
    public async Task TK10_newline_applies_to_the_whole_invocation(string newline, string expected)
    {
        var result = await Tk.RunAsync("print", "--out", "x", "--newline", newline, "--out", "y");

        await Assert.That(result.Stdout).IsEqualTo(expected);
    }

    [Test]
    public async Task TK10_fill_writes_63_x_and_lf_repeated_cut_at_size()
    {
        var result = await Tk.RunAsync("print", "--out-fill", "130", "--err-fill", "64");

        var line = new string('x', 63) + "\n";
        await Assert.That(result.Stdout).IsEqualTo(line + line + "xx");
        await Assert.That(result.Stderr).IsEqualTo(line);
    }

    [Test]
    public async Task TK10_exit_sets_the_code_and_no_operations_write_nothing()
    {
        var result = await Tk.RunAsync("print", "--exit", "7");

        await Assert.That(result.ExitCode).IsEqualTo(7);
        await Assert.That(result.StdoutBytes).IsEmpty();
        await Assert.That(result.Stderr).IsEmpty();
    }

    [Test]
    public async Task TK10_exit_and_hang_together_are_a_usage_error()
    {
        var result = await Tk.RunAsync("print", "--exit", "1", "--hang");

        await Assert.That(result.ExitCode).IsEqualTo(125);
    }

    [Test]
    public async Task TK10_a_write_after_close_exits_124()
    {
        var result = await Tk.RunAsync("print", "--out", "a", "--close-out", "--out", "b");

        await Assert.That(result.ExitCode).IsEqualTo(124);
        await Assert.That(result.Stdout).IsEqualTo("a\n");
    }

    [Test]
    [NotInParallel("timing")]
    public async Task TK10_close_out_ends_the_stream_for_the_reader_before_exit()
    {
        // The reader (cat) reaches EOF when stdout closes, long before the 3s sleep ends.
        var result = await Tk.ShellAsync("\"$TK\" print --out a --close-out --sleep 3s --exit 0 | { cat; echo eof; }; echo done");

        await Assert.That(result.Stdout).IsEqualTo("a\neof\ndone\n");
    }

    [Test]
    [NotInParallel("timing")]
    public async Task TK10_sleep_pauses_between_operations()
    {
        var result = await Tk.RunAsync("print", "--out", "a", "--sleep", "300ms", "--out", "b");

        await Assert.That(result.Elapsed).IsGreaterThanOrEqualTo(TimeSpan.FromMilliseconds(300));
        await Assert.That(result.Stdout).IsEqualTo("a\nb\n");
    }

    // TK-11: cat.
    [Test]
    public async Task TK11_cat_copies_stdin_byte_for_byte()
    {
        var input = new byte[] { 0, 1, 0xff, 0x0d, 0x0a, 0xef, 0xbb, 0xbf };
        var result = await Tk.RunAsync(new TkOptions { Stdin = input }, "cat");

        await Assert.That(result.StdoutBytes).IsEquivalentTo(input);
    }

    [Test]
    public async Task TK11_cat_to_stderr()
    {
        var result = await Tk.RunAsync(new TkOptions { Stdin = "hi"u8.ToArray() }, "cat", "--to", "stderr");

        await Assert.That(result.Stderr).IsEqualTo("hi");
        await Assert.That(result.StdoutBytes).IsEmpty();
    }

    [Test]
    public async Task TK11_cat_with_empty_or_null_stdin_exits_at_once()
    {
        var empty = await Tk.RunAsync("cat");
        var devNull = await Tk.ShellAsync("\"$TK\" cat </dev/null; echo $?");

        await Assert.That(empty.ExitCode).IsEqualTo(0);
        await Assert.That(empty.StdoutBytes).IsEmpty();
        await Assert.That(devNull.Stdout).IsEqualTo("0\n");
    }

    // TK-12: argv.
    [Test]
    public async Task TK12_argv_prints_every_argument_as_a_json_array()
    {
        var result = await Tk.RunAsync("argv", "a b", "-x", "", "--", "--out", "\"q\"\\");

        await Assert.That(result.Stdout).IsEqualTo("[\"a b\",\"-x\",\"\",\"--\",\"--out\",\"\\\"q\\\"\\\\\"]\n");
    }

    [Test]
    public async Task TK12_argv_with_no_arguments_prints_an_empty_array()
    {
        var result = await Tk.RunAsync("argv");

        await Assert.That(result.Stdout).IsEqualTo("[]\n");
    }

    [Test]
    public async Task TK12_argv_raw_exits_123_on_unix()
    {
        if (OperatingSystem.IsWindows())
        {
            Skip.Test("Unix behaviour");
        }

        var result = await Tk.RunAsync("argv", "--raw");

        await Assert.That(result.ExitCode).IsEqualTo(123);
    }

    // TK-13: env and getenv.
    [Test]
    public async Task TK13_env_prints_named_variables_in_argument_order_with_null_for_unset()
    {
        var options = new TkOptions();
        options.Env["TK_B"] = "two";
        options.Env["TK_A"] = "";
        options.Env["TK_UNSET"] = null;
        var result = await Tk.RunAsync(options, "env", "TK_B", "TK_UNSET", "TK_A");

        await Assert.That(result.Stdout).IsEqualTo("{\"TK_B\":\"two\",\"TK_UNSET\":null,\"TK_A\":\"\"}\n");
    }

    [Test]
    public async Task TK13_env_all_prints_every_variable_sorted_by_name()
    {
        var options = new TkOptions();
        options.Env["TK_ZZ"] = "z";
        options.Env["TK_AA"] = "a";
        var result = await Tk.RunAsync(options, "env", "--all");

        using var json = JsonDocument.Parse(result.Stdout);
        var names = json.RootElement.EnumerateObject().Select(p => p.Name).ToList();
        await Assert.That(names).IsEquivalentTo(names.Order(StringComparer.Ordinal).ToList());
        await Assert.That(json.RootElement.GetProperty("TK_AA").GetString()).IsEqualTo("a");
        await Assert.That(json.RootElement.GetProperty("TK_ZZ").GetString()).IsEqualTo("z");
    }

    [Test]
    public async Task TK13_env_names_are_case_sensitive_on_unix()
    {
        if (OperatingSystem.IsWindows())
        {
            Skip.Test("Unix behaviour");
        }

        var options = new TkOptions();
        options.Env["TK_CASE"] = "x";
        var result = await Tk.RunAsync(options, "env", "tk_case");

        await Assert.That(result.Stdout).IsEqualTo("{\"tk_case\":null}\n");
    }

    [Test]
    public async Task TK13_getenv_prints_the_value_or_exits_3()
    {
        var options = new TkOptions();
        options.Env["TK_SET"] = "value";
        options.Env["TK_UNSET"] = null;
        var set = await Tk.RunAsync(options, "getenv", "TK_SET");
        var unset = await Tk.RunAsync(options, "getenv", "TK_UNSET");

        await Assert.That(set.Stdout).IsEqualTo("value\n");
        await Assert.That(unset.ExitCode).IsEqualTo(3);
        await Assert.That(unset.StdoutBytes).IsEmpty();
    }

    // TK-17: info.
    [Test]
    public async Task TK17_info_reports_process_platform_and_stdin()
    {
        using var dir = new TempDir();
        var result = await Tk.RunAsync(dir.Options(), "info");
        var devNull = await Tk.ShellAsync("\"$TK\" info </dev/null");
        var file = await Tk.ShellAsync("\"$TK\" info <\"$TK\"");

        using var json = JsonDocument.Parse(result.Stdout);
        var root = json.RootElement;
        await Assert.That(root.EnumerateObject().Select(p => p.Name).ToArray()).IsEquivalentTo(
            new[] { "pid", "ppid", "user", "uid", "euid", "elevated", "cwd", "os", "arch", "tty", "stdin" });
        await Assert.That(root.GetProperty("ppid").GetInt32()).IsEqualTo(Environment.ProcessId);
        await Assert.That(root.GetProperty("user").GetString()).IsEqualTo(Environment.UserName);
        await Assert.That(root.GetProperty("cwd").GetString()).IsEqualTo(dir.Path);
        var os = OperatingSystem.IsLinux() ? "linux" : OperatingSystem.IsWindows() ? "windows" : "macos";
        await Assert.That(root.GetProperty("os").GetString()).IsEqualTo(os);
        await Assert.That(root.GetProperty("arch").GetString()).IsIn("x64", "arm64");
        await Assert.That(root.GetProperty("elevated").GetBoolean()).IsEqualTo(Environment.IsPrivilegedProcess);
        await Assert.That(root.GetProperty("stdin").GetString()).IsEqualTo("pipe");
        await Assert.That(JsonElement.Parse(devNull.Stdout).GetProperty("stdin").GetString()).IsEqualTo("null-device");
        await Assert.That(JsonElement.Parse(file.Stdout).GetProperty("stdin").GetString()).IsEqualTo("file");
    }

    [Test]
    public async Task TK17_info_without_a_controlling_terminal_reports_tty_false()
    {
        if (!OperatingSystem.IsLinux())
        {
            Skip.Test("Linux behaviour");
        }

        // setsid(1) starts it in a new session, which has no controlling terminal.
        var result = await Tk.ShellAsync("setsid \"$TK\" info");

        await Assert.That(JsonElement.Parse(result.Stdout).GetProperty("tty").GetBoolean()).IsFalse();
    }

    // TK-18: write-file and read-file.
    [Test]
    public async Task TK18_write_file_text_hex_append_and_newline()
    {
        using var dir = new TempDir();
        var options = dir.Options();
        await Tk.RunAsync(options, "write-file", "f.txt", "--text", "a");
        await Tk.RunAsync(options, "write-file", "f.txt", "--text", "b", "--append", "--newline", "crlf");
        await Tk.RunAsync(options, "write-file", "f.txt", "--hex", "00ff", "--append");
        await Tk.RunAsync(options, "write-file", "g.txt", "--text", "x", "--newline", "none");
        await Tk.RunAsync(options, "write-file", "g.txt", "--text", "y");

        await Assert.That(File.ReadAllBytes(dir.File("f.txt"))).IsEquivalentTo(new byte[] { (byte)'a', 10, (byte)'b', 13, 10, 0, 0xff });
        await Assert.That(File.ReadAllText(dir.File("g.txt"))).IsEqualTo("y\n");
    }

    [Test]
    public async Task TK18_write_file_creates_no_parent_directories()
    {
        using var dir = new TempDir();
        var result = await Tk.RunAsync(dir.Options(), "write-file", "missing/f.txt", "--text", "a");

        await Assert.That(result.ExitCode).IsEqualTo(124);
        await Assert.That(Directory.Exists(dir.File("missing"))).IsFalse();
    }

    [Test]
    [Arguments("write-file f")]
    [Arguments("write-file f --text a --hex 00")]
    public async Task TK18_write_file_needs_exactly_one_of_text_and_hex(string argLine)
    {
        var args = argLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        using var dir = new TempDir();
        var result = await Tk.RunAsync(dir.Options(), args);

        await Assert.That(result.ExitCode).IsEqualTo(125);
    }

    [Test]
    public async Task TK18_read_file_copies_bytes_or_exits_3()
    {
        using var dir = new TempDir();
        File.WriteAllBytes(dir.File("b.bin"), [1, 0, 0xfe]);
        var found = await Tk.RunAsync(dir.Options(), "read-file", "b.bin");
        var missing = await Tk.RunAsync(dir.Options(), "read-file", "nope.bin");

        await Assert.That(found.StdoutBytes).IsEquivalentTo(new byte[] { 1, 0, 0xfe });
        await Assert.That(missing.ExitCode).IsEqualTo(3);
    }

    // TK-19: count and flaky.
    [Test]
    public async Task TK19_count_increments_from_zero()
    {
        using var dir = new TempDir();
        var first = await Tk.RunAsync(dir.Options(), "count", "--state", "n.txt");
        var second = await Tk.RunAsync(dir.Options(), "count", "--state", "n.txt");

        await Assert.That(first.Stdout).IsEqualTo("1\n");
        await Assert.That(second.Stdout).IsEqualTo("2\n");
        await Assert.That(second.ExitCode).IsEqualTo(0);
    }

    [Test]
    public async Task TK19_concurrent_counts_each_get_a_distinct_number()
    {
        using var dir = new TempDir();
        var results = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => Tk.RunAsync(dir.Options(), "count", "--state", "n.txt")));

        var values = results.Select(r => int.Parse(r.Stdout, CultureInfo.InvariantCulture)).Order().ToArray();
        await Assert.That(values).IsEquivalentTo(Enumerable.Range(1, 20).ToArray());
    }

    [Test]
    public async Task TK19_flaky_fails_n_times_then_succeeds()
    {
        using var dir = new TempDir();
        var codes = new List<int>();
        for (var i = 0; i < 4; i++)
        {
            codes.Add((await Tk.RunAsync(dir.Options(), "flaky", "--state", "a.txt", "--fail-times", "2", "--code", "9")).ExitCode);
        }

        var defaultCode = await Tk.RunAsync(dir.Options(), "flaky", "--state", "b.txt", "--fail-times", "1");

        await Assert.That(codes).IsEquivalentTo(new[] { 9, 9, 0, 0 });
        await Assert.That(defaultCode.ExitCode).IsEqualTo(1);
        await Assert.That(defaultCode.Stdout).IsEqualTo("1\n");
    }

    [Test]
    public async Task TK19_a_state_file_without_an_integer_exits_124()
    {
        using var dir = new TempDir();
        File.WriteAllText(dir.File("n.txt"), "abc");
        var result = await Tk.RunAsync(dir.Options(), "count", "--state", "n.txt");

        await Assert.That(result.ExitCode).IsEqualTo(124);
    }

    // TK-22: symlink.
    [Test]
    public async Task TK22_symlink_creates_a_link_to_a_target_that_need_not_exist()
    {
        if (OperatingSystem.IsWindows())
        {
            Skip.Test("Unix behaviour");
        }

        using var dir = new TempDir();
        var result = await Tk.RunAsync(dir.Options(), "symlink", "missing.txt", "dangling");

        await Assert.That(result.ExitCode).IsEqualTo(0);
        await Assert.That(new FileInfo(dir.File("dangling")).LinkTarget).IsEqualTo("missing.txt");
    }

    // TK-23: chmod.
    [Test]
    public async Task TK23_chmod_sets_the_permission_bits()
    {
        if (OperatingSystem.IsWindows())
        {
            Skip.Test("Unix behaviour");
            return;
        }

        using var dir = new TempDir();
        File.WriteAllText(dir.File("f"), "x");
        var locked = await Tk.RunAsync(dir.Options(), "chmod", "000", "f");
        var mode000 = File.GetUnixFileMode(dir.File("f"));
        var open = await Tk.RunAsync(dir.Options(), "chmod", "0754", "f");

        await Assert.That(locked.ExitCode).IsEqualTo(0);
        await Assert.That(mode000).IsEqualTo(UnixFileMode.None);
        await Assert.That(open.ExitCode).IsEqualTo(0);
        await Assert.That((int)File.GetUnixFileMode(dir.File("f"))).IsEqualTo(Convert.ToInt32("754", 8));
    }

    [Test]
    [Arguments("8")]
    [Arguments("rwx")]
    [Arguments("17777")]
    public async Task TK23_chmod_mode_must_be_octal(string mode)
    {
        using var dir = new TempDir();
        File.WriteAllText(dir.File("f"), "x");
        var result = await Tk.RunAsync(dir.Options(), "chmod", mode, "f");

        await Assert.That(result.ExitCode).IsEqualTo(125);
    }

}
