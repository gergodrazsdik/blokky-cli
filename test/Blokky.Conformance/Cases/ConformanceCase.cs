namespace Blokky.Conformance.Cases;

/// <summary>The kind of outcome a case expects (CF-25).</summary>
internal enum ExpectKind
{
    Validation,
    Run,
    NoResult,
}

/// <summary>Environment requirements (§3.2, CF-13).</summary>
internal sealed record Requirements(
    IReadOnlyList<string> Os,
    IReadOnlyList<string> Shells,
    IReadOnlyList<int> Ports,
    bool ContainerEngine)
{
    public static Requirements None { get; } = new([], [], [], false);
}

/// <summary>A runner action during the run (CF-22).</summary>
/// <param name="After">When, in plan time.</param>
/// <param name="Kill">True for <c>kill</c> (SIGKILL), false for <c>stop</c> (the external stop).</param>
internal sealed record CaseAction(TimeSpan After, bool Kill);

/// <summary>A check after the run (CF-64): a testkit invocation or a second plan.</summary>
internal sealed record AfterRunCheck(
    IReadOnlyList<string>? Argv,
    object? ExitCode,
    byte[]? Plan,
    IReadOnlyList<string> Inputs,
    OrderedDictionary<string, object?>? Expect,
    ExpectKind ExpectKind);

/// <summary>A file or directory to create in the work directory (CF-20).</summary>
/// <param name="Content">The bytes, or null for a directory.</param>
/// <param name="Executable">Mode 0755 on Unix.</param>
internal sealed record CaseFile(byte[]? Content, bool Executable);

/// <summary>
/// A case after the platform overrides for the current OS are applied (CF-33, CF-34). The raw expectations stay in the
/// value model of <see cref="Yaml.YamlValues"/>; the matcher reads them.
/// </summary>
internal sealed record ConformanceCase
{
    /// <summary>The path below <c>cases/</c> without <c>.yaml</c> (CF-7).</summary>
    public required string Id { get; init; }

    public required string Title { get; init; }

    public required IReadOnlyList<string> Rules { get; init; }

    public required IReadOnlyList<string> Spec { get; init; }

    public required IReadOnlyList<string> Features { get; init; }

    public required Requirements Requires { get; init; }

    /// <summary>The <c>clock</c> key, or null when the case doesn't set it (the default depends on the case, CF-39, CF-40).</summary>
    public required IReadOnlyList<string>? Clock { get; init; }

    /// <summary>The plan bytes exactly as passed to the implementation (CF-15).</summary>
    public required byte[] Plan { get; init; }

    public required IReadOnlyList<string> Inputs { get; init; }

    /// <summary>Variables to add (a value) or remove (null) from the base environment (CF-19).</summary>
    public required IReadOnlyDictionary<string, string?> Env { get; init; }

    public required IReadOnlyDictionary<string, CaseFile> Files { get; init; }

    public required IReadOnlyDictionary<string, IReadOnlyList<string>> Programs { get; init; }

    public required IReadOnlyList<CaseAction> Actions { get; init; }

    public required IReadOnlyList<AfterRunCheck> AfterRun { get; init; }

    public required OrderedDictionary<string, object?> Expect { get; init; }

    public required ExpectKind ExpectKind { get; init; }

    /// <summary>The <c>skip</c> reason of the platform override that applies, if any (CF-33).</summary>
    public string? PlatformSkip { get; init; }
}
