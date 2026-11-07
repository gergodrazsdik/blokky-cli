using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace Blokky.Testkit.Tests;

/// <summary>The outcome of one testkit invocation.</summary>
internal sealed record TkResult(int ExitCode, byte[] StdoutBytes, string Stderr, TimeSpan Elapsed)
{
    public string Stdout => Encoding.UTF8.GetString(StdoutBytes);
}

/// <summary>Runs the testkit binary as a real process (docs/design/testkit.md §3).</summary>
internal static partial class Tk
{
    /// <summary>The testkit under test: <c>BLOKKY_TESTKIT</c> (the published AOT binary), else the JIT build.</summary>
    public static string Path { get; } = Locate();

    /// <summary>Whether the binary is self-contained (Native AOT), so it can be copied or hard-linked elsewhere.</summary>
    public static bool IsSelfContained =>
        !File.Exists(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Path)!, "blokky-testkit.dll"));

    public static Task<TkResult> RunAsync(params string[] args) => RunAsync(new TkOptions(), args);

    public static async Task<TkResult> RunAsync(TkOptions options, params string[] args)
    {
        var timer = Stopwatch.StartNew();
        using var process = Start(options, args);
        var stdin = process.StandardInput.BaseStream;
        await stdin.WriteAsync(options.Stdin ?? []);
        stdin.Close();
        using var stdout = new MemoryStream();
        var copy = process.StandardOutput.BaseStream.CopyToAsync(stdout);
        var stderr = process.StandardError.ReadToEndAsync();
        try
        {
            await process.WaitForExitAsync().WaitAsync(options.Timeout);
        }
        catch (TimeoutException)
        {
            process.Kill(entireProcessTree: true);
            throw;
        }

        await copy;
        return new TkResult(process.ExitCode, stdout.ToArray(), await stderr, timer.Elapsed);
    }

    /// <summary>Starts the testkit with all three streams redirected; the caller drives it.</summary>
    public static Process Start(TkOptions options, params string[] args)
    {
        var psi = new ProcessStartInfo(options.Program ?? Path)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = options.WorkingDirectory ?? Environment.CurrentDirectory,
        };
        foreach (var arg in args)
        {
            psi.ArgumentList.Add(arg);
        }

        psi.Environment.Remove("TESTKIT_LOG");
        psi.Environment.Remove("TESTKIT_TIME_SCALE");
        foreach (var (name, value) in options.Env)
        {
            if (value is null)
            {
                psi.Environment.Remove(name);
            }
            else
            {
                psi.Environment[name] = value;
            }
        }

        return Process.Start(psi)!;
    }

    /// <summary>Runs <c>sh -c SCRIPT</c> with <c>$TK</c> set to the testkit, for merged pipes and reader behaviour.</summary>
    public static async Task<TkResult> ShellAsync(string script, string? workingDirectory = null)
    {
        var options = new TkOptions { Program = "/bin/sh", WorkingDirectory = workingDirectory };
        options.Env["TK"] = Path;
        return await RunAsync(options, "-c", script);
    }

    public static void Signal(int pid, int signal)
    {
        if (Kill(pid, signal) != 0)
        {
            throw new InvalidOperationException($"kill({pid}, {signal}) failed");
        }
    }

    public static bool IsAlive(int pid)
    {
        try
        {
            var stat = File.ReadAllText($"/proc/{pid}/stat");
            return stat[(stat.LastIndexOf(')') + 2)] != 'Z';
        }
        catch (IOException)
        {
            return false;
        }
    }

    /// <summary>A field of <c>/proc/PID/stat</c> after the command (field 4 is ppid, 6 is the session id).</summary>
    public static int StatField(int pid, int field)
    {
        var stat = File.ReadAllText($"/proc/{pid}/stat");
        var rest = stat[(stat.LastIndexOf(')') + 2)..].Split(' ');
        return int.Parse(rest[field - 3], System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>Waits until the condition holds or the timeout ends.</summary>
    public static async Task<bool> EventuallyAsync(Func<bool> condition, TimeSpan? timeout = null)
    {
        var deadline = Stopwatch.StartNew();
        while (deadline.Elapsed < (timeout ?? TimeSpan.FromSeconds(10)))
        {
            if (condition())
            {
                return true;
            }

            await Task.Delay(20);
        }

        return condition();
    }

    [LibraryImport("libc", EntryPoint = "kill")]
    private static partial int Kill(int pid, int signal);

    private static string Locate()
    {
        var fromEnv = Environment.GetEnvironmentVariable("BLOKKY_TESTKIT");
        if (!string.IsNullOrEmpty(fromEnv))
        {
            return fromEnv;
        }

        // artifacts/bin/Blokky.Testkit.Tests/<pivot>/ -> artifacts/bin/Blokky.Testkit/<pivot>/blokky-testkit
        var own = new DirectoryInfo(AppContext.BaseDirectory.TrimEnd(System.IO.Path.DirectorySeparatorChar));
        var name = OperatingSystem.IsWindows() ? "blokky-testkit.exe" : "blokky-testkit";
        return System.IO.Path.Combine(own.Parent!.Parent!.FullName, "Blokky.Testkit", own.Name, name);
    }
}

/// <summary>How to run one invocation.</summary>
internal sealed class TkOptions
{
    public string? Program { get; init; }

    public string? WorkingDirectory { get; init; }

    public byte[]? Stdin { get; init; }

    public Dictionary<string, string?> Env { get; } = new(StringComparer.Ordinal);

    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(30);
}

/// <summary>A fresh temporary directory, removed on dispose.</summary>
internal sealed class TempDir : IDisposable
{
    public string Path { get; } = Directory.CreateTempSubdirectory("blokky-testkit-").FullName;

    public string File(string name) => System.IO.Path.Combine(Path, name);

    public TkOptions Options() => new() { WorkingDirectory = Path };

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
            // Best effort.
        }
        catch (UnauthorizedAccessException)
        {
            // Best effort (chmod tests).
        }
    }
}
