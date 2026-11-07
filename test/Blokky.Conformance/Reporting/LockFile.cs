using System.Diagnostics;

namespace Blokky.Conformance.Reporting;

/// <summary>
/// <c>conformance.lock</c>: the blokky-specs version the runner is measured against (ADR 0004, ci-cd.md §2).
/// Lines of <c>key: value</c>; <c>#</c> starts a comment. Keys: <c>repository</c>, <c>tag</c>, <c>commit</c>.
/// </summary>
internal sealed record LockFile(string? Repository, string? Tag, string? Commit)
{
    public static LockFile Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            var colon = line.IndexOf(':', StringComparison.Ordinal);
            if (colon <= 0)
            {
                throw new FormatException($"conformance.lock: '{line}' isn't 'key: value'");
            }

            values[line[..colon].Trim()] = line[(colon + 1)..].Trim();
        }

        if (values.Keys.FirstOrDefault(k => k is not ("repository" or "tag" or "commit")) is { } unknown)
        {
            throw new FormatException($"conformance.lock: unknown key '{unknown}'");
        }

        return new LockFile(values.GetValueOrDefault("repository"), values.GetValueOrDefault("tag"), values.GetValueOrDefault("commit"));
    }

    /// <summary>The checked-out commit of <paramref name="specsDir"/>, or null if it isn't a git checkout.</summary>
    public static string? HeadOf(string specsDir)
    {
        try
        {
            var psi = new ProcessStartInfo("git") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
            foreach (var arg in new[] { "-C", specsDir, "rev-parse", "HEAD" })
            {
                psi.ArgumentList.Add(arg);
            }

            using var git = Process.Start(psi);
            if (git is null)
            {
                return null;
            }

            var head = git.StandardOutput.ReadToEnd().Trim();
            git.WaitForExit();
            return git.ExitCode == 0 && head.Length > 0 ? head : null;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }
}
