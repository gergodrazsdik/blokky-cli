using Blokky.Core.Ast;
using Blokky.Core.Syntax;

namespace Blokky.Core.Validation.Rules.Syntax;

/// <summary>
/// The syntax rule family (T-0082): grammars of strings that the schema can't express, each checked by the parser the
/// executor uses too (<see cref="Blokky.Core.Syntax"/>). Exec strings (SH-7) and batch files as argv[0] (SH-10); regexes
/// within the portable subset and its size bound (RF-22, SY-61, SY-62, SY-104); JSON paths (SY-65 to SY-68); globs (SY-70
/// to SY-73, SY-102); image references (SY-103). Each value gets at most one error, <c>BK_PLAN_VALUE</c> at the value's
/// start (SY-1), since the string alone decides it. A value with <c>${</c> is RF-9's mistake (interpolation in a literal
/// field, errors.md §4): the grammars skip it, except an exec string, whose other SH-7 problems are still reported.
/// </summary>
public sealed class SyntaxRules : IPlanRule
{
    /// <inheritdoc/>
    public IReadOnlyList<string> Rules { get; } =
        ["SH-7", "SH-10", "RF-22", "SY-65", "SY-66", "SY-67", "SY-68", "SY-70", "SY-71", "SY-72", "SY-73", "SY-102", "SY-103"];

    /// <inheritdoc/>
    public void Check(PlanContext context, PlanErrorSink errors)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(errors);
        foreach (var input in context.Ast.Inputs ?? [])
        {
            Regex(input.Matches, errors);
        }

        foreach (var node in PlanWalk.Nodes(context.Ast))
        {
            Exec(node.Exec, errors);
            foreach (var service in node.Services ?? [])
            {
                Exec(service.Exec, errors);
            }

            foreach (var field in node.Capture?.Fields ?? [])
            {
                Json(field.Path, errors);
            }

            if (node.For is { } loop)
            {
                Glob(loop.Glob, errors);
                Json(loop.ItemsJson, errors);
                foreach (var field in loop.Fields ?? [])
                {
                    Json(field.Path, errors);
                }
            }

            Image(node.Container?.Image, errors);

            foreach (var (_, condition) in PlanWalk.Conditions(node))
            {
                foreach (var c in PlanWalk.Within(condition))
                {
                    CheckCondition(c, errors);
                }
            }
        }
    }

    private static void CheckCondition(Condition condition, PlanErrorSink errors)
    {
        Json(condition.Json, errors);
        foreach (var op in condition.Operators)
        {
            if (op.Kind is OperatorKind.Matches or OperatorKind.NotMatches && op.Operand is LiteralValue literal)
            {
                Regex(literal.Scalar, errors);
            }
        }

        foreach (var subject in condition.Subjects)
        {
            Exec(subject.Probe?.Exec, errors);
        }
    }

    private static void Exec(ExecSpec? exec, PlanErrorSink errors)
    {
        if (exec?.Command is { } command)
        {
            if (ExecString.Split(command.Text, out var argv) is { } splitError)
            {
                Report(command, splitError, errors);
            }
            else if (ExecString.BatchFile(argv[0]) is { } batch)
            {
                Report(command, batch, errors);
            }
        }
        else if (exec?.Argv is [LiteralValue program, ..] && ExecString.BatchFile(program.Scalar.Text) is { } batch)
        {
            Report(program.Scalar, batch, errors);
        }
    }

    private static void Image(PlanScalar? image, PlanErrorSink errors)
    {
        if (image is not null && !IsInterpolated(image) && ImageReference.Parse(image.Text, out var error) is null)
        {
            Report(image, error!, errors);
        }
    }

    private static void Regex(PlanScalar? pattern, PlanErrorSink errors)
    {
        if (pattern is not null && !IsInterpolated(pattern) && PortableRegex.Check(pattern.Text) is { } error)
        {
            Report(pattern, error, errors);
        }
    }

    private static void Json(PlanScalar? path, PlanErrorSink errors)
    {
        if (path is not null && !IsInterpolated(path) && JsonPath.Parse(path.Text, out _) is { } error)
        {
            Report(path, error, errors);
        }
    }

    private static void Glob(PlanScalar? pattern, PlanErrorSink errors)
    {
        if (pattern is not null && !IsInterpolated(pattern) && GlobPattern.Parse(pattern.Text, out _) is { } error)
        {
            Report(pattern, error, errors);
        }
    }

    /// <summary>Whether the value has a <c>${</c>, which RF-9 reports in a literal field.</summary>
    private static bool IsInterpolated(PlanScalar value) => value.Text.Contains("${", StringComparison.Ordinal);

    private static void Report(PlanScalar value, GrammarError error, PlanErrorSink errors) =>
        errors.Value(error.Rule, value.Position, value.Node, error.Message, error.Fix);
}
