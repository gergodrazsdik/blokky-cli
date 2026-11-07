using System.Globalization;
using System.Text;
using System.Text.Json;
using Blokky.Conformance.Cases;

namespace Blokky.Conformance.Execution;

/// <summary>
/// One case's directories (CF-56, testkit.md §4): <c>work/</c> is the run's cwd and holds only the case's
/// <c>files</c>; <c>bin/</c>, <c>home/</c>, <c>tmp/</c> and the invocation log live next to it.
/// </summary>
internal sealed class CaseWorkspace : IDisposable
{
    public const string TestkitName = "blokky-testkit";

    private readonly bool _keep;

    private CaseWorkspace(string root, bool keep)
    {
        Root = root;
        _keep = keep;
        Directory.CreateDirectory(WorkDir);
        Directory.CreateDirectory(BinDir);
        Directory.CreateDirectory(HomeDir);
        Directory.CreateDirectory(TmpDir);
    }

    public string Root { get; }

    public string WorkDir => Path.Combine(Root, "work");

    public string BinDir => Path.Combine(Root, "bin");

    public string HomeDir => Path.Combine(Root, "home");

    public string TmpDir => Path.Combine(Root, "tmp");

    public string LogPath => Path.Combine(Root, "testkit.log");

    /// <summary>The testkit inside <c>bin/</c>, once <see cref="Populate"/> linked it.</summary>
    public string? Testkit { get; private set; }

    public static CaseWorkspace Create(string runRoot, string caseId, bool keep)
    {
        var name = caseId.Replace('/', '.');
        return new CaseWorkspace(Path.Combine(runRoot, name), keep);
    }

    /// <summary>
    /// Creates the case's files (CF-20) and, if the testkit exists, links it and the aliases into <c>bin/</c> (CF-21, CF-58).
    /// </summary>
    public void Populate(ConformanceCase c, string? testkit)
    {
        ArgumentNullException.ThrowIfNull(c);
        foreach (var (relative, file) in c.Files)
        {
            var full = Path.Combine(WorkDir, relative.Replace('/', Path.DirectorySeparatorChar));
            if (file.Content is null)
            {
                Directory.CreateDirectory(full);
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllBytes(full, file.Content);
            if (file.Executable && !OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(full, (UnixFileMode)Convert.ToInt32("755", 8));
            }
        }

        if (testkit is null)
        {
            return;
        }

        var exe = OperatingSystem.IsWindows() ? ".exe" : "";
        Testkit = Path.Combine(BinDir, TestkitName + exe);
        LinkOrCopy(testkit, Testkit);
        foreach (var (alias, args) in c.Programs)
        {
            LinkOrCopy(testkit, Path.Combine(BinDir, alias + exe));
            // TK-6: one argument per line, LF-separated, no trailing empty argument.
            File.WriteAllText(Path.Combine(BinDir, alias + ".testkit-args"), string.Join('\n', args), new UTF8Encoding(false));
        }
    }

    /// <summary>The base environment (CF-57), then the case's <c>env</c> (CF-19).</summary>
    public Dictionary<string, string> Environment(ConformanceCase c, HostInfo host, double timeScale)
    {
        ArgumentNullException.ThrowIfNull(c);
        ArgumentNullException.ThrowIfNull(host);
        var path = new List<string> { BinDir };
        path.AddRange(host.Shells.Values.Distinct(StringComparer.Ordinal));
        var env = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["PATH"] = string.Join(Path.PathSeparator, path.Distinct(StringComparer.Ordinal)),
            ["HOME"] = HomeDir,
            ["TMPDIR"] = TmpDir,
            ["TESTKIT_LOG"] = LogPath,
            ["TESTKIT_TIME_SCALE"] = timeScale.ToString("0.######", CultureInfo.InvariantCulture),
        };
        if (host.Os == "windows")
        {
            env["USERPROFILE"] = HomeDir;
            env["TEMP"] = TmpDir;
            env["TMP"] = TmpDir;
            foreach (var name in new[] { "SystemRoot", "PATHEXT", "COMSPEC" })
            {
                if (System.Environment.GetEnvironmentVariable(name) is { } value)
                {
                    env[name] = value;
                }
            }
        }

        var passthrough = new List<string>();
        if (c.Requires.Shells.Any(s => s is "pwsh" or "powershell"))
        {
            passthrough.AddRange(HostInfo.ShellPassthrough);
        }

        if (c.Requires.ContainerEngine)
        {
            passthrough.AddRange(HostInfo.EnginePassthrough);
        }

        foreach (var name in passthrough)
        {
            if (host.Passthrough.TryGetValue(name, out var value))
            {
                env[name] = value;
            }
        }

        foreach (var (name, value) in c.Env)
        {
            if (value is null)
            {
                env.Remove(name);
            }
            else
            {
                env[name] = value;
            }
        }

        return env;
    }

    /// <summary>The invocation log's entries (TK-5), from line <paramref name="skip"/> on.</summary>
    public List<object?> ReadLog(int skip = 0)
    {
        var entries = new List<object?>();
        if (!File.Exists(LogPath))
        {
            return entries;
        }

        foreach (var line in File.ReadAllLines(LogPath, Encoding.UTF8).Skip(skip))
        {
            if (line.Length == 0)
            {
                continue;
            }

            using var doc = JsonDocument.Parse(line);
            entries.Add(JsonValues.From(doc.RootElement));
        }

        return entries;
    }

    public int LogCount() => File.Exists(LogPath) ? File.ReadAllLines(LogPath).Count(l => l.Length > 0) : 0;

    public void Dispose()
    {
        if (_keep)
        {
            return;
        }

        try
        {
            if (!OperatingSystem.IsWindows())
            {
                // chmod 000 by a case (TK-23) mustn't stop the cleanup.
                MakeTraversable(Root);
            }

            Directory.Delete(Root, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best effort; the run root is a temporary directory.
        }
    }

    [System.Runtime.Versioning.UnsupportedOSPlatform("windows")]
    private static void MakeTraversable(string dir)
    {
        File.SetUnixFileMode(dir, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        foreach (var sub in Directory.EnumerateDirectories(dir))
        {
            if (new DirectoryInfo(sub).LinkTarget is null)
            {
                MakeTraversable(sub);
            }
        }
    }

    private static void LinkOrCopy(string source, string target)
    {
        if (!OperatingSystem.IsWindows() && Interop.Link(source, target) == 0)
        {
            return;
        }

        // Not a symlink: TK-6 resolves a symlink's directory to the shared testkit dir (testkit.md §4).
        File.Copy(source, target, overwrite: true);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(target, (UnixFileMode)Convert.ToInt32("755", 8));
        }
    }
}
