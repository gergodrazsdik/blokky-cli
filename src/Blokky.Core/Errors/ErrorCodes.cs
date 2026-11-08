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

    /// <summary>Not UTF-8: a UTF-16/32 BOM or invalid UTF-8 (language, SY-6, SY-7).</summary>
    public const string PlanEncoding = "BK_PLAN_ENCODING";

    /// <summary>A character the character lint rejects (language, SY-12).</summary>
    public const string PlanCharacter = "BK_PLAN_CHARACTER";

    /// <summary>Over 4 MiB, or nested deeper than 128 levels (language, SY-9).</summary>
    public const string PlanTooLarge = "BK_PLAN_TOO_LARGE";

    /// <summary>Not valid YAML, or a rejected YAML feature (language, SY-14 to SY-17).</summary>
    public const string PlanYaml = "BK_PLAN_YAML";

    /// <summary>A key repeated in a mapping (language, SY-18).</summary>
    public const string PlanDuplicateKey = "BK_PLAN_DUPLICATE_KEY";

    /// <summary>A key that no kind or position accepts in that kind of mapping (language, SY-19, SY-30).</summary>
    public const string PlanUnknownKey = "BK_PLAN_UNKNOWN_KEY";

    /// <summary><c>blokky:</c> missing, malformed or naming an unsupported version (language, SY-34, SY-35, SY-51).</summary>
    public const string PlanVersion = "BK_PLAN_VERSION";

    /// <summary>A value that doesn't match its field's type or grammar (language).</summary>
    public const string PlanValue = "BK_PLAN_VALUE";

    /// <summary>A language key in the wrong place, or a rule relating keys, values or scopes (language).</summary>
    public const string PlanInvalid = "BK_PLAN_INVALID";

    /// <summary>A <c>{ from_env: NAME }</c> names an unset environment variable (language, VA-40).</summary>
    public const string FromEnvUnset = "BK_FROM_ENV_UNSET";

    /// <summary>A feature the implementation doesn't support yet (language).</summary>
    public const string PlanNotSupported = "BK_PLAN_NOT_SUPPORTED";

    /// <summary>A bug in Blokky (CLI).</summary>
    public const string Internal = "BK_INTERNAL";
}
