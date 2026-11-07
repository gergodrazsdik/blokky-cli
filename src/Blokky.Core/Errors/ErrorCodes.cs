namespace Blokky.Core.Errors;

/// <summary>
/// Built-in error codes used so far. The full catalogues are blokky-specs <c>spec/errors.md</c> (language)
/// and docs/contracts/errors.md (CLI).
/// </summary>
public static class ErrorCodes
{
    /// <summary>Malformed command-line flags or arguments (CLI).</summary>
    public const string Usage = "BK_USAGE";

    /// <summary>A malformed <c>--input</c> (language).</summary>
    public const string InputSyntax = "BK_INPUT_SYNTAX";

    /// <summary>A feature the implementation doesn't support yet (language).</summary>
    public const string PlanNotSupported = "BK_PLAN_NOT_SUPPORTED";

    /// <summary>A bug in Blokky (CLI).</summary>
    public const string Internal = "BK_INTERNAL";
}
