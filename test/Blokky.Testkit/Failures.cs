namespace Blokky.Testkit;

/// <summary>Exit codes the testkit reserves for itself (TK §1.1).</summary>
internal static class ExitCodes
{
    public const int NotSupported = 123;
    public const int Internal = 124;
    public const int Usage = 125;
}

/// <summary>Ends the invocation with one of the reserved exit codes and a <c>blokky-testkit:</c> line on stderr (TK-4).</summary>
internal sealed class TestkitException(int exitCode, string message) : Exception(message)
{
    public int ExitCode { get; } = exitCode;

    public static TestkitException Usage(string message) => new(ExitCodes.Usage, message);

    public static TestkitException Internal(string message) => new(ExitCodes.Internal, message);

    public static TestkitException NotSupported(string what) => new(ExitCodes.NotSupported, $"{what} is not supported on this OS");
}
