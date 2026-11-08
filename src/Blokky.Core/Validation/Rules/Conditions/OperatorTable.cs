using Blokky.Core.Ast;

namespace Blokky.Core.Validation.Rules.Conditions;

/// <summary>The operator table of conditions.md §3 (CO-47, CO-12, CO-10).</summary>
internal static class OperatorTable
{
    /// <summary>
    /// Whether <paramref name="op"/> takes <paramref name="subject"/> (CO-47). For <c>result</c>, <paramref name="part"/> is
    /// the part (<c>stdout</c> …), or null when it is invalid and was reported already: then any part the operator takes passes.
    /// </summary>
    public static bool Takes(OperatorKind op, SubjectKind subject, string? part) => op switch
    {
        OperatorKind.EqualsTo or OperatorKind.NotEquals or OperatorKind.In or OperatorKind.NotIn =>
            subject is SubjectKind.Var or SubjectKind.Env or SubjectKind.Result or SubjectKind.Probe or SubjectKind.Host,
        OperatorKind.Contains or OperatorKind.NotContains or OperatorKind.Matches or OperatorKind.NotMatches =>
            subject is SubjectKind.Var or SubjectKind.Env or SubjectKind.Probe or SubjectKind.Output
            || (subject == SubjectKind.Result && part is null or "stdout" or "stderr"),
        OperatorKind.Empty or OperatorKind.NotEmpty =>
            subject is SubjectKind.Var or SubjectKind.Env or SubjectKind.Probe
            || (subject == SubjectKind.Result && part is null or "stdout" or "stderr" or "error"),
        OperatorKind.Defined or OperatorKind.NotDefined => subject is SubjectKind.Var or SubjectKind.Env,
        OperatorKind.Exists or OperatorKind.NotExists => subject is SubjectKind.Path or SubjectKind.Command,
        OperatorKind.IsFile or OperatorKind.IsDir => subject == SubjectKind.Path,
        OperatorKind.Open => subject == SubjectKind.Port,
        OperatorKind.Gt or OperatorKind.Ge or OperatorKind.Lt or OperatorKind.Le =>
            subject is SubjectKind.Var or SubjectKind.Env or SubjectKind.Probe
            || (subject == SubjectKind.Result && part is null or "stdout" or "exit"),
        _ => true,
    };

    /// <summary>The valid subjects of <paramref name="op"/>, for messages.</summary>
    public static string Describe(OperatorKind op) => op switch
    {
        OperatorKind.EqualsTo or OperatorKind.NotEquals or OperatorKind.In or OperatorKind.NotIn => "var, env, result, probe or host",
        OperatorKind.Contains or OperatorKind.NotContains or OperatorKind.Matches or OperatorKind.NotMatches =>
            "var, env, result (stdout, stderr), probe or output",
        OperatorKind.Empty or OperatorKind.NotEmpty => "var, env, result (stdout, stderr, error) or probe",
        OperatorKind.Defined or OperatorKind.NotDefined => "var or env",
        OperatorKind.Exists or OperatorKind.NotExists => "path or command",
        OperatorKind.IsFile or OperatorKind.IsDir => "path",
        OperatorKind.Open => "port",
        _ => "var, env, result (stdout, exit) or probe",
    };

    /// <summary>Whether <c>ignore_case</c> applies to <paramref name="op"/> (CO-12).</summary>
    public static bool TakesIgnoreCase(OperatorKind op) => op is OperatorKind.EqualsTo or OperatorKind.NotEquals or OperatorKind.In
        or OperatorKind.NotIn or OperatorKind.Contains or OperatorKind.NotContains or OperatorKind.Matches or OperatorKind.NotMatches;

    /// <summary>The <c>not_*</c> form of <paramref name="op"/> or the operator it negates; null when there is none (CO-10).</summary>
    public static string? Opposite(OperatorKind op) => op switch
    {
        OperatorKind.EqualsTo => "not_equals",
        OperatorKind.NotEquals => "equals",
        OperatorKind.In => "not_in",
        OperatorKind.NotIn => "in",
        OperatorKind.Contains => "not_contains",
        OperatorKind.NotContains => "contains",
        OperatorKind.Matches => "not_matches",
        OperatorKind.NotMatches => "matches",
        OperatorKind.Empty => "not_empty",
        OperatorKind.NotEmpty => "empty",
        OperatorKind.Defined => "not_defined",
        OperatorKind.NotDefined => "defined",
        OperatorKind.Exists => "not_exists",
        OperatorKind.NotExists => "exists",
        _ => null,
    };
}
