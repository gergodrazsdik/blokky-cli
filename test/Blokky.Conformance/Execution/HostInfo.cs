namespace Blokky.Conformance.Execution;

/// <summary>What the machine provides, found once when the runner starts (CF-13, CF-57).</summary>
/// <param name="Os">The OS key: <c>linux</c>, <c>macos</c> or <c>windows</c>.</param>
/// <param name="Shells">Installed shells (<c>sh</c>, <c>bash</c>, …) and the directory each was found in.</param>
/// <param name="Passthrough">Runner variables passed through per requirement (CF-57), as found at start.</param>
/// <param name="Testkit">The <c>blokky-testkit</c> executable, or null when it isn't built.</param>
/// <param name="ContainerEngine">Whether a container engine and the suite's images are available (CF-66).</param>
internal sealed record HostInfo(
    string Os,
    IReadOnlyDictionary<string, string> Shells,
    IReadOnlyDictionary<string, string> Passthrough,
    string? Testkit,
    bool ContainerEngine)
{
    /// <summary>Shells the suite can require (<c>requires.shells</c>).</summary>
    public static readonly IReadOnlyList<string> KnownShells = ["sh", "bash", "pwsh", "powershell", "zsh"];

    /// <summary>Variables passed through for <c>shells: [pwsh]</c> or <c>[powershell]</c>.</summary>
    public static readonly IReadOnlyList<string> ShellPassthrough = ["DOTNET_ROOT"];

    /// <summary>Variables passed through for <c>container-engine</c>.</summary>
    public static readonly IReadOnlyList<string> EnginePassthrough = ["DOCKER_HOST", "XDG_RUNTIME_DIR"];

    public static string CurrentOs =>
        OperatingSystem.IsWindows() ? "windows" : OperatingSystem.IsMacOS() ? "macos" : "linux";

    /// <summary>Looks the shells up on the runner's own <c>PATH</c>.</summary>
    public static HostInfo Detect(string? testkit, bool containerEngine)
    {
        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        var dirs = path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
        var shells = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var shell in KnownShells)
        {
            var name = OperatingSystem.IsWindows() ? shell + ".exe" : shell;
            if (dirs.FirstOrDefault(d => File.Exists(Path.Combine(d, name))) is { } dir)
            {
                shells[shell] = dir;
            }
        }

        var passthrough = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var name in ShellPassthrough.Concat(EnginePassthrough))
        {
            if (Environment.GetEnvironmentVariable(name) is { Length: > 0 } value)
            {
                passthrough[name] = value;
            }
        }

        return new HostInfo(CurrentOs, shells, passthrough, testkit is not null && File.Exists(testkit) ? Path.GetFullPath(testkit)
            : null, containerEngine);
    }
}
