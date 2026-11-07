namespace Blokky.Core.Features;

/// <summary>
/// The pinned feature vocabulary of the conformance suite (blokky-specs <c>conformance/README.md</c> §3.2, CF-12).
/// These are also the values of <c>BK_PLAN_NOT_SUPPORTED</c>'s <c>detail.feature</c>.
/// </summary>
public static class FeatureIds
{
    /// <summary>Node kinds, in vocabulary order.</summary>
    public static readonly IReadOnlyList<string> Kinds =
    [
        "exec", "exec-string", "script", "do", "all", "any", "race", "if", "sleep", "log", "fail", "assert", "set", "return",
    ];

    /// <summary>Every feature id, in vocabulary order.</summary>
    public static readonly IReadOnlyList<string> All =
    [
        .. Kinds,
        "shell-bash", "shell-sh", "shell-pwsh", "shell-powershell", "shell-zsh",
        "stdin", "ok-exit", "stderr-merge", "encoding", "keep-descendants", "keep-descendants-detach",
        "capture", "capture-fields", "capture-file", "secret",
        "when", "timeout", "lock", "tempdir", "cwd", "env", "export", "container", "services", "finally",
        "ignore-errors", "attempt-timeout",
        "retry", "retry-condition", "backoff", "jitter", "while", "until", "for-each", "for-count", "for-glob",
        "for-items", "for-lines", "for-matrix", "for-concurrency", "collect", "carry", "index",
        "cond-var", "cond-env", "cond-path", "cond-command", "cond-host", "cond-result", "cond-probe", "cond-json",
        "cond-numeric", "cond-composite",
        "from-env", "secret-ref", "interpolation",
        "inputs", "vars", "outputs", "defaults",
        "external-stop", "kill",
    ];

    /// <summary>Whether <paramref name="id"/> is in the vocabulary (ordinal).</summary>
    public static bool IsKnown(string id) => All.Contains(id, StringComparer.Ordinal);
}
