using System.Collections.Frozen;

namespace Blokky.Core.Validation.Rules.Variables;

/// <summary>
/// The environment names the plan can't set or pass (variables.md §7.2, §7.3): RF-10's forbidden <c>env</c> keys and
/// reserved <c>export</c> names, which <c>container.env_pass</c> follows too (CP-42). Names compare case-insensitively
/// on Windows (VA-6).
/// </summary>
public static class EnvironmentNames
{
    private static readonly StringComparison Comparison =
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private static readonly FrozenSet<string> Forbidden = FrozenSet.Create(Comparer, "PATH", "BASH_ENV", "ENV", "SHELLOPTS", "BASHOPTS");

    private static readonly FrozenSet<string> ReservedNames = FrozenSet.Create(Comparer,
        // VA-42.
        "PATH", "HOME", "USER", "SHELL", "IFS", "PWD", "TEMP", "BASH_ENV", "ENV", "PATHEXT", "COMSPEC", "SystemRoot",
        // §7.3, the execution-influencing names.
        "NODE_OPTIONS", "NODE_PATH", "PYTHONPATH", "PYTHONSTARTUP", "PYTHONHOME", "PERL5OPT", "PERL5LIB", "RUBYOPT", "RUBYLIB",
        "JAVA_TOOL_OPTIONS", "JDK_JAVA_OPTIONS", "_JAVA_OPTIONS", "DOTNET_STARTUP_HOOKS", "DOTNET_ADDITIONAL_DEPS",
        "GIT_SSH_COMMAND", "GIT_SSH", "GIT_EXEC_PATH", "GIT_ASKPASS", "SSH_ASKPASS", "GIT_PROXY_COMMAND", "GIT_EXTERNAL_DIFF",
        "DOCKER_HOST", "DOCKER_CONFIG", "KUBECONFIG", "PSModulePath", "PAGER", "GIT_PAGER", "EDITOR", "VISUAL", "PERL5DB",
        "R_PROFILE", "R_PROFILE_USER");

    // VA-42 (TMP*, which covers TMP and TMPDIR; PS*, which covers PS1 to PS4; BLOKKY_*) and §7.3.
    private static readonly string[] ReservedPrefixes =
        ["TMP", "PS", "LD_", "DYLD_", "BLOKKY_", "BASH_FUNC_", "CORECLR_PROFILER", "COR_PROFILER", "GIT_CONFIG_", "npm_config_"];

    private static StringComparer Comparer => OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    /// <summary>How names compare on this host (VA-6).</summary>
    public static StringComparer NameComparer => Comparer;

    /// <summary>Whether <c>env</c> may not set <paramref name="name"/> (VA-41).</summary>
    public static bool IsForbidden(string name) => Forbidden.Contains(name);

    /// <summary>Whether <c>export</c> may not name <paramref name="name"/> (VA-42 and the §7.3 list).</summary>
    public static bool IsReserved(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return ReservedNames.Contains(name)
            || ReservedPrefixes.Any(p => name.StartsWith(p, Comparison))
            || name.EndsWith("_OPTIONS", Comparison);
    }
}
