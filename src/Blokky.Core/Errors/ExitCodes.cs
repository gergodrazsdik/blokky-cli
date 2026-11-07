namespace Blokky.Core.Errors;

/// <summary>
/// Exit codes (cli.md "Exit codes") and the one place that maps a failing pre-run step to its code
/// (docs/contracts/errors.md §1). Hosts don't pick exit codes themselves.
/// </summary>
public static class ExitCodes
{
    /// <summary>Success.</summary>
    public const int Success = 0;

    /// <summary>The run finished and a node failed.</summary>
    public const int PlanFailed = 1;

    /// <summary>Malformed flags or arguments, <c>--input</c> errors.</summary>
    public const int Usage = 2;

    /// <summary>The plan is invalid (including not supported).</summary>
    public const int PlanInvalid = 3;

    /// <summary>Config error.</summary>
    public const int Config = 4;

    /// <summary>Approval needed.</summary>
    public const int ApprovalNeeded = 5;

    /// <summary>Rejected by the validator or runner-rules mode.</summary>
    public const int Rejected = 6;

    /// <summary><c>--expect-sha256</c> mismatch.</summary>
    public const int ShaMismatch = 7;

    /// <summary>A bug in Blokky.</summary>
    public const int Internal = 70;

    /// <summary>The exit code for a pre-run failure in <paramref name="phase"/>.</summary>
    /// <param name="phase">The failing step.</param>
    /// <param name="errors">The step's errors; the approval step's code depends on them.</param>
    public static int ForPhase(Phase phase, IReadOnlyList<BlokkyError> errors)
    {
        ArgumentNullException.ThrowIfNull(errors);
        return phase switch
        {
            Phase.Usage => Usage,
            Phase.Plan => PlanInvalid,
            Phase.Config => Config,
            Phase.Approval when errors.Any(e => e.Code == "BK_SHA_MISMATCH") => ShaMismatch,
            Phase.Approval when errors.Any(e => e.Code == "BK_DENIED") => Rejected,
            Phase.Approval => ApprovalNeeded,
            _ => throw new ArgumentOutOfRangeException(nameof(phase), phase, "unknown phase"),
        };
    }
}
