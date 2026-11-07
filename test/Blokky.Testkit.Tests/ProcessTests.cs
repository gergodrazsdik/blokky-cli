using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;

namespace Blokky.Testkit.Tests;

/// <summary>testkit-interface.md: signals (TK-8, TK-14, TK-21), process trees (TK-15, TK-16) and listen (TK-20). Linux for 0.1.</summary>
[NotInParallel("timing")]
public class ProcessTests
{
    private const int SigHup = 1;
    private const int SigInt = 2;
    private const int SigKill = 9;
    private const int SigTerm = 15;

    // TK-8: default dispositions; graceful stop requests end the testkit.
    [Test]
    [Arguments(SigTerm)]
    [Arguments(SigInt)]
    [Arguments(SigHup)]
    public async Task TK8_stop_requests_end_a_hanging_print(int signal)
    {
        SkipUnlessLinux();
        using var process = Tk.Start(new TkOptions(), "print", "--out", "ready", "--hang");
        await process.StandardOutput.ReadLineAsync();
        Tk.Signal(process.Id, signal);
        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));

        // .NET reports a termination by signal N as 128 + N.
        await Assert.That(process.ExitCode).IsEqualTo(128 + signal);
    }

    // TK-14: sleep.
    [Test]
    public async Task TK14_sleep_sleeps_then_exits_with_the_code()
    {
        var result = await Tk.RunAsync("sleep", "200ms", "--exit", "3");

        await Assert.That(result.ExitCode).IsEqualTo(3);
        await Assert.That(result.Elapsed).IsGreaterThanOrEqualTo(TimeSpan.FromMilliseconds(200));
    }

    [Test]
    public async Task TK14_ignore_term_ignores_and_records_every_request_until_the_forced_kill()
    {
        SkipUnlessLinux();
        using var dir = new TempDir();
        using var process = Tk.Start(dir.Options(), "sleep", "1m", "--ignore-term", "--record-signals", "sig.txt", "--pid-file", "pid");
        await Tk.EventuallyAsync(() => File.Exists(dir.File("pid")));
        await Task.Delay(200);
        Tk.Signal(process.Id, SigTerm);
        await Tk.EventuallyAsync(() => File.Exists(dir.File("sig.txt")));
        Tk.Signal(process.Id, SigInt);
        Tk.Signal(process.Id, SigHup);
        await Tk.EventuallyAsync(() => File.ReadAllLines(dir.File("sig.txt")).Length == 3);
        var aliveAfterRequests = !process.HasExited;
        Tk.Signal(process.Id, SigKill);
        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));

        await Assert.That(aliveAfterRequests).IsTrue();
        await Assert.That(File.ReadAllLines(dir.File("sig.txt")).Order(StringComparer.Ordinal).ToArray())
            .IsEquivalentTo(new[] { "SIGHUP", "SIGINT", "SIGTERM" });
        await Assert.That(process.ExitCode).IsEqualTo(128 + SigKill);
    }

    [Test]
    public async Task TK14_ignore_term_for_ignores_only_early_requests()
    {
        SkipUnlessLinux();
        using var dir = new TempDir();
        using var process = Tk.Start(
            dir.Options(), "sleep", "1m", "--ignore-term-for", "1s", "--record-signals", "sig.txt", "--tag", "db", "--pid-file", "pid");
        await Tk.EventuallyAsync(() => File.Exists(dir.File("pid")));
        await Task.Delay(200);
        Tk.Signal(process.Id, SigTerm);
        await Tk.EventuallyAsync(() => File.Exists(dir.File("sig.txt")));
        await Task.Delay(1200);
        var aliveAfterFirst = !process.HasExited;
        Tk.Signal(process.Id, SigTerm);
        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));

        await Assert.That(aliveAfterFirst).IsTrue();
        await Assert.That(File.ReadAllText(dir.File("sig.txt"))).IsEqualTo("db SIGTERM\ndb SIGTERM\n");
        await Assert.That(process.ExitCode).IsEqualTo(128 + SigTerm);
    }

    [Test]
    [Arguments("pid")]
    [Arguments("log")]
    public async Task TK14_a_request_right_after_the_pid_file_or_log_line_is_already_ignored(string waitFor)
    {
        SkipUnlessLinux();

        // No grace delay: the handlers are registered before the log line and the pid file are written.
        for (var round = 0; round < 5; round++)
        {
            using var dir = new TempDir();
            var options = dir.Options();
            options.Env["TESTKIT_LOG"] = dir.File("log");
            using var process = Tk.Start(options, "sleep", "1m", "--ignore-term", "--record-signals", "sig.txt", "--pid-file", "pid");
            try
            {
                await Tk.EventuallyAsync(() => File.Exists(dir.File(waitFor)) && new FileInfo(dir.File(waitFor)).Length > 0);
                Tk.Signal(process.Id, SigTerm);
                await Tk.EventuallyAsync(() => File.Exists(dir.File("sig.txt")));
                await Task.Delay(50);

                await Assert.That(process.HasExited).IsFalse();
                await Assert.That(File.ReadAllText(dir.File("sig.txt"))).IsEqualTo("SIGTERM\n");
            }
            finally
            {
                process.Kill();
                await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            }
        }
    }

    [Test]
    public async Task TK14_record_signals_without_ignore_records_then_ends()
    {
        SkipUnlessLinux();
        using var dir = new TempDir();
        using var process = Tk.Start(dir.Options(), "sleep", "1m", "--record-signals", "sig.txt", "--pid-file", "pid");
        await Tk.EventuallyAsync(() => File.Exists(dir.File("pid")));
        await Task.Delay(200);
        Tk.Signal(process.Id, SigTerm);
        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));

        await Assert.That(File.ReadAllText(dir.File("sig.txt"))).IsEqualTo("SIGTERM\n");
        await Assert.That(process.ExitCode).IsEqualTo(128 + SigTerm);
    }

    // TK-15: spawn.
    [Test]
    public async Task TK15_a_tree_of_count_and_depth_runs_the_command_at_the_leaves()
    {
        SkipUnlessLinux();
        using var dir = new TempDir();
        var options = dir.Options();
        options.Env["TESTKIT_LOG"] = dir.File("log.jsonl");
        var result = await Tk.RunAsync(
            options, "spawn", "--count", "2", "--depth", "2", "--pids", "pids.txt", "--wait", "--", "print", "--out", "leaf");

        await Assert.That(result.ExitCode).IsEqualTo(0);
        await Assert.That(result.Stdout).IsEqualTo("leaf\nleaf\nleaf\nleaf\n");

        // count^1 + count^2 descendants, each logged (TK-5).
        await Assert.That(File.ReadAllLines(dir.File("pids.txt")).Length).IsEqualTo(6);
        await Assert.That(File.ReadAllLines(dir.File("log.jsonl")).Length).IsEqualTo(7);
    }

    [Test]
    public async Task TK15_wait_exits_with_the_first_non_zero_child_code()
    {
        SkipUnlessLinux();
        var result = await Tk.RunAsync("spawn", "--count", "2", "--wait", "--", "exit", "7");

        await Assert.That(result.ExitCode).IsEqualTo(7);
    }

    [Test]
    public async Task TK15_without_wait_exits_once_children_started_and_they_hold_the_output()
    {
        SkipUnlessLinux();

        // The reader sees "late" although spawn itself exits at once (EM-20).
        var result = await Tk.ShellAsync("\"$TK\" spawn -- print --sleep 300ms --out late; echo \"spawn=$?\"");
        var held = await Tk.ShellAsync("\"$TK\" spawn -- print --sleep 300ms --out late | cat");

        await Assert.That(result.Stdout).IsEqualTo("spawn=0\nlate\n");
        await Assert.That(held.Stdout).IsEqualTo("late\n");
        await Assert.That(held.Elapsed).IsGreaterThanOrEqualTo(TimeSpan.FromMilliseconds(300));
    }

    [Test]
    public async Task TK15_release_output_gives_descendants_the_null_device()
    {
        SkipUnlessLinux();
        using var dir = new TempDir();
        var result = await Tk.ShellAsync(
            "\"$TK\" spawn --release-output --pids pids.txt -- print --sleep 2s --out late | cat; echo end", dir.Path);
        var pid = int.Parse(File.ReadAllText(dir.File("pids.txt")), CultureInfo.InvariantCulture);
        var stillRunning = Tk.IsAlive(pid);
        Tk.Signal(pid, SigKill);

        await Assert.That(result.Stdout).IsEqualTo("end\n");
        await Assert.That(result.Elapsed).IsLessThan(TimeSpan.FromSeconds(1.5));
        await Assert.That(stillRunning).IsTrue();
    }

    [Test]
    public async Task TK15_escape_setsid_starts_children_in_a_new_session()
    {
        SkipUnlessLinux();
        using var dir = new TempDir();
        await Tk.RunAsync(dir.Options(), "spawn", "--escape", "setsid", "--release-output", "--pids", "pids.txt", "--", "sleep", "5s");
        var pid = int.Parse(File.ReadAllText(dir.File("pids.txt")), CultureInfo.InvariantCulture);
        var session = Tk.StatField(pid, 6);
        Tk.Signal(pid, SigKill);

        await Assert.That(session).IsEqualTo(pid);
    }

    [Test]
    public async Task TK15_escape_double_fork_reparents_the_child_away()
    {
        SkipUnlessLinux();
        using var dir = new TempDir();
        await Tk.RunAsync(dir.Options(), "spawn", "--escape", "double-fork", "--release-output", "--pids", "pids.txt", "--", "sleep", "5s");
        await Tk.EventuallyAsync(() => File.ReadAllLines(dir.File("pids.txt")).Length == 2);
        var pids = File.ReadAllLines(dir.File("pids.txt")).Select(l => int.Parse(l, CultureInfo.InvariantCulture)).ToArray();
        var daemon = pids[1];
        await Tk.EventuallyAsync(() => !Tk.IsAlive(pids[0]));
        var parent = Tk.StatField(daemon, 4);
        var session = Tk.StatField(daemon, 6);
        Tk.Signal(daemon, SigKill);

        await Assert.That(parent).IsNotEqualTo(pids[0]);
        await Assert.That(session).IsEqualTo(daemon);
    }

    [Test]
    public async Task TK15_breakaway_is_not_supported_on_unix()
    {
        SkipUnlessLinux();
        var result = await Tk.RunAsync("spawn", "--escape", "breakaway", "--", "exit", "0");

        await Assert.That(result.ExitCode).IsEqualTo(123);
    }

    [Test]
    [Arguments("spawn exit 0")]
    [Arguments("spawn --")]
    [Arguments("spawn --escape fork -- exit 0")]
    [Arguments("spawn --count 0 -- exit 0")]
    public async Task TK15_spawn_usage_errors(string argLine)
    {
        var args = argLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var result = await Tk.RunAsync(args);

        await Assert.That(result.ExitCode).IsEqualTo(125);
    }

    // TK-16: alive.
    [Test]
    public async Task TK16_alive_lists_live_pids_and_exits_1()
    {
        SkipUnlessLinux();
        using var dir = new TempDir();
        using var sleeper = Tk.Start(new TkOptions(), "sleep", "1m");
        using var done = Tk.Start(new TkOptions(), "exit", "0");
        await done.WaitForExitAsync();
        File.WriteAllText(dir.File("pids.txt"), $"{sleeper.Id}\n{done.Id}\n");
        var alive = await Tk.RunAsync(dir.Options(), "alive", "pids.txt");
        sleeper.Kill();
        await sleeper.WaitForExitAsync();
        var none = await Tk.RunAsync(dir.Options(), "alive", "pids.txt");

        await Assert.That(alive.Stdout).IsEqualTo($"[{sleeper.Id}]\n");
        await Assert.That(alive.ExitCode).IsEqualTo(1);
        await Assert.That(none.Stdout).IsEqualTo("[]\n");
        await Assert.That(none.ExitCode).IsEqualTo(0);
    }

    [Test]
    public async Task TK16_a_zombie_does_not_count_as_alive()
    {
        SkipUnlessLinux();
        using var dir = new TempDir();

        // The shell execs sleep, which never reaps the shell's background child: once it exits it stays a zombie.
        var options = new TkOptions { Program = "/bin/sh", WorkingDirectory = dir.Path };
        options.Env["TK"] = Tk.Path;
        using var parent = Tk.Start(options, "-c", "\"$TK\" exit 0 & echo $! > pids.txt; exec sleep 10");
        try
        {
            await Tk.EventuallyAsync(() => File.Exists(dir.File("pids.txt")) && File.ReadAllText(dir.File("pids.txt")).EndsWith('\n'));
            var pid = File.ReadAllText(dir.File("pids.txt")).Trim();
            var zombie = await Tk.EventuallyAsync(() => ProcState(pid) == 'Z');

            // The premise: the pid is a zombie, not gone (then the test would prove nothing).
            await Assert.That(zombie).IsTrue();

            var result = await Tk.RunAsync(dir.Options(), "alive", "pids.txt");

            await Assert.That(ProcState(pid)).IsEqualTo('Z');
            await Assert.That(result.Stdout).IsEqualTo("[]\n");
            await Assert.That(result.ExitCode).IsEqualTo(0);
        }
        finally
        {
            parent.Kill();
            await parent.WaitForExitAsync();
        }
    }

    /// <summary>The state letter in <c>/proc/&lt;pid&gt;/stat</c> (after the parenthesised name), or null if it's gone.</summary>
    private static char? ProcState(string pid)
    {
        try
        {
            var stat = File.ReadAllText($"/proc/{pid}/stat");
            return stat[(stat.LastIndexOf(')') + 2)..][0];
        }
        catch (IOException)
        {
            return null;
        }
    }

    [Test]
    public async Task TK16_a_missing_pid_file_exits_124()
    {
        using var dir = new TempDir();
        var result = await Tk.RunAsync(dir.Options(), "alive", "nope.txt");

        await Assert.That(result.ExitCode).IsEqualTo(124);
    }

    // TK-20: listen.
    [Test]
    public async Task TK20_listen_accepts_connections_after_the_ready_line()
    {
        var port = FreePort();
        using var process = Tk.Start(
            new TkOptions(), "listen", "--port", port.ToString(CultureInfo.InvariantCulture), "--after", "200ms", "--out", "listening");
        var line = await process.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(10));
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, port);
        var read = await client.GetStream().ReadAsync(new byte[1]).AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        process.Kill();

        await Assert.That(line).IsEqualTo("listening");
        await Assert.That(read).IsEqualTo(0);
    }

    [Test]
    public async Task TK20_listen_exit_after_exits_with_the_code()
    {
        var port = FreePort().ToString(CultureInfo.InvariantCulture);
        var result = await Tk.RunAsync("listen", "--port", port, "--exit-after", "200ms", "--exit", "5");

        await Assert.That(result.ExitCode).IsEqualTo(5);
        await Assert.That(result.StdoutBytes).IsEmpty();
    }

    [Test]
    public async Task TK20_a_port_that_cant_be_bound_exits_4()
    {
        using var taken = new TcpListener(IPAddress.Loopback, 0);
        taken.Start();
        var port = ((IPEndPoint)taken.LocalEndpoint).Port;
        var result = await Tk.RunAsync("listen", "--port", port.ToString(CultureInfo.InvariantCulture), "--exit-after", "1s");

        await Assert.That(result.ExitCode).IsEqualTo(4);
        await Assert.That(result.Stderr).IsNotEmpty();
    }

    // TK-21: die.
    [Test]
    [Arguments("SIGTERM", 15)]
    [Arguments("SIGKILL", 9)]
    [Arguments("SIGSEGV", 11)]
    [Arguments("SIGABRT", 6)]
    public async Task TK21_die_terminates_by_the_signal(string name, int number)
    {
        SkipUnlessLinux();
        using var dir = new TempDir();

        // The shell's $? can't tell a signal from an exit code; kill -l names the signal only for 128 + N.
        var result = await Tk.ShellAsync($"\"$TK\" die --signal {name}; echo $?", dir.Path);

        await Assert.That(result.Stdout).IsEqualTo($"{128 + number}\n");
        await Assert.That(Directory.GetFiles(dir.Path)).IsEmpty();
    }

    [Test]
    public async Task TK21_an_unknown_signal_is_a_usage_error()
    {
        var result = await Tk.RunAsync("die", "--signal", "SIGNOPE");

        await Assert.That(result.ExitCode).IsEqualTo(OperatingSystem.IsWindows() ? 123 : 125);
    }

    [Test]
    public async Task TK8_TK17_the_log_shows_the_spawn_parent_as_ppid()
    {
        SkipUnlessLinux();
        using var dir = new TempDir();
        var options = dir.Options();
        options.Env["TESTKIT_LOG"] = dir.File("log.jsonl");
        await Tk.RunAsync(options, "spawn", "--wait", "--", "exit", "0");

        var lines = File.ReadAllLines(dir.File("log.jsonl")).Select(l => JsonElement.Parse(l)).ToArray();
        await Assert.That(lines.Length).IsEqualTo(2);
        await Assert.That(lines[1].GetProperty("ppid").GetInt32()).IsEqualTo(lines[0].GetProperty("pid").GetInt32());
    }

    private static int FreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private static void SkipUnlessLinux()
    {
        if (!OperatingSystem.IsLinux())
        {
            Skip.Test("Linux behaviour (0.1)");
        }
    }
}
