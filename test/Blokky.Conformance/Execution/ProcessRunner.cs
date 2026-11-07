using System.Diagnostics;

namespace Blokky.Conformance.Execution;

/// <summary>A runner action at a point in real time (CF-22, already converted from plan time).</summary>
/// <param name="At">When, after the process started.</param>
/// <param name="Kill">SIGKILL (<c>kill</c>), or else the external stop (SIGTERM).</param>
internal sealed record TimedAction(TimeSpan At, bool Kill);

/// <summary>One process to run.</summary>
internal sealed record ProcessSpec(
    string FileName,
    IReadOnlyList<string> Args,
    IReadOnlyDictionary<string, string> Env,
    string Cwd,
    byte[] Stdin,
    TimeSpan Deadline,
    IReadOnlyList<TimedAction> Actions);

/// <summary>What happened.</summary>
/// <param name="Killed">A <c>kill</c> action fired.</param>
/// <param name="DeadlineHit">The harness deadline fired and the process tree was killed (CF-44).</param>
internal sealed record ProcessOutcome(int ExitCode, byte[] Stdout, byte[] Stderr, bool Killed, bool DeadlineHit, TimeSpan Elapsed);

/// <summary>Starts processes; a seam for the adapter's unit tests.</summary>
internal interface IProcessRunner
{
    Task<ProcessOutcome> RunAsync(ProcessSpec spec, CancellationToken cancellationToken);
}

/// <summary>Runs a process with an environment built from nothing (CF-57), its stdin, a deadline and timed actions.</summary>
internal sealed class ProcessRunner : IProcessRunner
{
    /// <summary>util-linux <c>setsid</c>, where it exists (Linux).</summary>
    private static readonly string? Setsid = OperatingSystem.IsLinux()
        ? new[] { "/usr/bin/setsid", "/bin/setsid" }.FirstOrDefault(File.Exists)
        : null;

    public async Task<ProcessOutcome> RunAsync(ProcessSpec spec, CancellationToken cancellationToken)
    {
        // Its own session (and so process group) on Linux: util-linux setsid(1) calls setsid() and execs in place, since a
        // child of the runner never leads a group, so the pid stays the program's. Terminal and runner group signals miss it.
        var psi = new ProcessStartInfo(Setsid ?? spec.FileName)
        {
            WorkingDirectory = spec.Cwd,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        if (Setsid is not null)
        {
            psi.ArgumentList.Add(spec.FileName);
        }

        foreach (var arg in spec.Args)
        {
            psi.ArgumentList.Add(arg);
        }

        psi.Environment.Clear();
        foreach (var (name, value) in spec.Env)
        {
            psi.Environment[name] = value;
        }

        var clock = Stopwatch.StartNew();
        using var process = await StartAsync(psi, cancellationToken).ConfigureAwait(false);
        var stdout = Drain(process.StandardOutput.BaseStream);
        var stderr = Drain(process.StandardError.BaseStream);
        var stdin = Feed(process.StandardInput.BaseStream, spec.Stdin);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var killed = false;
        var actions = spec.Actions.Select(async action =>
        {
            try
            {
                await Task.Delay(action.At, TimeProvider.System, cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            if (process.HasExited)
            {
                return;
            }

            if (action.Kill)
            {
                killed = true;
                process.Kill();
            }
            else if (!OperatingSystem.IsWindows())
            {
                _ = Interop.Kill(process.Id, Interop.SigTerm);
            }
        }).ToList();

        var deadlineHit = false;
        using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
        {
            deadline.CancelAfter(spec.Deadline);
            try
            {
                await process.WaitForExitAsync(deadline.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                deadlineHit = !cancellationToken.IsCancellationRequested;
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
            }
        }

        await cts.CancelAsync().ConfigureAwait(false);
        await Task.WhenAll(actions).ConfigureAwait(false);
        await stdin.ConfigureAwait(false);

        // Descendants that hold the pipes open don't keep the runner waiting past the deadline.
        var output = await WithTimeout(stdout, TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        var error = await WithTimeout(stderr, TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        return new ProcessOutcome(process.ExitCode, output, error, killed, deadlineHit, clock.Elapsed);
    }

    /// <summary>
    /// Starts the process, retrying on ETXTBSY: a copied testkit can still be open for writing in a child that another
    /// thread forked during the copy (the workspace prefers hard links, which avoid it).
    /// </summary>
    private static async Task<Process> StartAsync(ProcessStartInfo psi, CancellationToken cancellationToken)
    {
        const int TextFileBusy = 26;
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return Process.Start(psi) ?? throw new InvalidOperationException($"can't start {psi.FileName}");
            }
            catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == TextFileBusy && attempt < 10)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(20 * attempt), TimeProvider.System, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private static async Task<byte[]> Drain(Stream stream)
    {
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer).ConfigureAwait(false);
        return buffer.ToArray();
    }

    private static async Task Feed(Stream stream, byte[] bytes)
    {
        try
        {
            await stream.WriteAsync(bytes).ConfigureAwait(false);
            await stream.FlushAsync().ConfigureAwait(false);
        }
        catch (IOException)
        {
            // The process exited without reading all of stdin.
        }
        finally
        {
            try
            {
                await stream.DisposeAsync().ConfigureAwait(false);
            }
            catch (IOException)
            {
                // Broken pipe on close.
            }
        }
    }

    private static async Task<byte[]> WithTimeout(Task<byte[]> task, TimeSpan timeout)
    {
        try
        {
            return await task.WaitAsync(timeout, TimeProvider.System).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            return [];
        }
    }
}
