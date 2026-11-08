using Blokky.Core.Ast;
using Blokky.Core.Reading;

namespace Blokky.Core.Validation.Rules.Conditions;

/// <summary>
/// The conditions rule family (conditions.md, reference.md §8; T-0084): the shape of a condition (CO-4), <c>ignore_case</c>
/// on operators that don't take it (CO-12), the probe shape (CO-26), the service-only subjects (CO-44), operator and
/// subject pairs (CO-47), the <c>result</c> subject (RF-13, which CO-5 restates) and <c>not:</c> around a simple condition
/// with a <c>not_*</c> form (RF-14, which CO-10 restates). Every check is linear in the conditions' keys.
/// </summary>
/// <remarks>
/// Not here: the grammars of operands, JSON paths and regexes (the AST builder and the syntax family, T-0082), the names a
/// <c>var:</c> subject references (the variables family, T-0083), and a string condition or a condition where a node list
/// is expected (CO-4, reported by the AST builder as <c>BK_PLAN_VALUE</c>).
/// </remarks>
public sealed class ConditionRules : IPlanRule
{
    /// <inheritdoc/>
    public IReadOnlyList<string> Rules { get; } = ["CO-4", "CO-12", "CO-26", "CO-44", "CO-47", "RF-13", "RF-14"];

    /// <inheritdoc/>
    public void Check(PlanContext context, PlanErrorSink errors)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(errors);
        foreach (var node in PlanWalk.Nodes(context.Ast))
        {
            foreach (var (field, top) in PlanWalk.Conditions(node))
            {
                foreach (var condition in PlanWalk.Within(top))
                {
                    Check(node, field, condition, errors);
                }
            }
        }
    }

    private static void Check(PlanNode node, string field, Condition condition, PlanErrorSink errors)
    {
        if (condition.NodeKindKey is { } kind)
        {
            errors.Invalid("CO-4", kind.Position, condition.Node,
                $"{field}: takes a condition, not a node ('{kind.Name}'): to run a command as a check, use a probe",
                $"{field}: {{ probe: {{ {kind.Name}: … }} }}");
            return;
        }

        if (!Shape(field, condition, errors))
        {
            NotAroundSimple(condition, errors);
            return;
        }

        foreach (var subject in condition.Subjects)
        {
            Subject(node, field, subject, condition.Node, errors);
        }

        if (condition.Subjects.Count > 0)
        {
            var subject = condition.Subjects[0];
            foreach (var op in condition.Operators)
            {
                Pairing(subject, op, condition.Node, errors);
            }
        }

        IgnoreCase(condition, errors);
    }

    /// <summary>
    /// CO-4: one subject and one operator, a bare probe, or one composite key alone. False for a composite, whose own keys
    /// are checked where they are conditions themselves, and for a mapping with no language key.
    /// </summary>
    private static bool Shape(string field, Condition condition, PlanErrorSink errors)
    {
        var keys = condition.Keys;
        if (keys.Count == 0)
        {
            // Every key was unknown: those errors say it all.
            return false;
        }

        var composite = keys.FirstOrDefault(k => k.Name is "all" or "any" or "not");
        if (composite is not null)
        {
            if (keys.Count > 1)
            {
                var first = keys[0];
                var extra = keys[1];
                errors.Invalid("CO-4", extra.Position, condition.Node,
                    $"{field}: '{extra.Name}' can't share a condition with '{first.Name}': a composite (all, any, not) stands alone; "
                    + "put each check in its own condition",
                    "{ all: [ { var: A, equals: x }, { env: B, defined: true } ] }");
            }

            return false;
        }

        var subjects = condition.Subjects;
        var operators = condition.Operators;
        if (subjects.Count > 1)
        {
            errors.Invalid("CO-4", subjects[1].Key.Position, condition.Node,
                $"{field}: a condition has one subject, but '{subjects[0].Key.Name}' and '{subjects[1].Key.Name}' are two: "
                + "write two conditions in all: or any:",
                "{ all: [ { var: A, equals: x }, { env: B, defined: true } ] }");
        }

        if (operators.Count > 1)
        {
            errors.Invalid("CO-4", operators[1].Key.Position, condition.Node,
                $"{field}: a condition has one operator, but '{operators[0].Key.Name}' and '{operators[1].Key.Name}' are two: "
                + "write two conditions in all: or any:",
                "{ all: [ { var: A, equals: x }, { var: A, contains: y } ] }");
        }

        if (subjects.Count == 0)
        {
            errors.Invalid("CO-4", condition.Position, condition.Node,
                $"{field}: the condition has no subject: name what it tests, such as var: NAME",
                $"{field}: {{ var: NAME, equals: value }}");
        }
        else if (operators.Count == 0)
        {
            var subject = subjects[0];
            if (subject.Kind != SubjectKind.Probe)
            {
                errors.Invalid("CO-4", subject.Key.Position, condition.Node,
                    $"{field}: '{subject.Key.Name}' needs an operator, such as equals: value or defined: true; only a probe stands alone",
                    $"{field}: {{ {subject.Key.Name}: …, equals: value }}");
            }
            else
            {
                // A bare probe is true or false by its exit code (CO-2): the modifiers of a simple condition don't apply.
                foreach (var modifier in keys.Where(k => k.Name is "json" or "ignore_case"))
                {
                    errors.Invalid("CO-4", modifier.Position, condition.Node,
                        $"{field}: '{modifier.Name}' applies to an operator, and a bare probe has none: it is true or false by "
                        + "its exit code; add an operator to test its output",
                        $"{field}: {{ probe: {{ exec: … }}, equals: value }}");
                }
            }
        }

        return true;
    }

    /// <summary>CO-26 (a probe's shape), CO-44 (port and output in ready only) and RF-13 (result).</summary>
    private static void Subject(PlanNode node, string field, ConditionSubject subject, string at, PlanErrorSink errors)
    {
        switch (subject.Kind)
        {
            case SubjectKind.Probe when subject.Probe is { } probe:
                Probe(probe, errors);
                break;
            case SubjectKind.Port or SubjectKind.Output when field != "services.ready":
                errors.Invalid("CO-44", subject.Key.Position, at,
                    $"{field}: '{subject.Key.Name}' is valid only in a service's ready: condition, where it refers to that service");
                break;
            case SubjectKind.Result when field is not ("until" or "retry"):
                errors.Invalid("RF-13", subject.Key.Position, at,
                    $"{field}: 'result' is valid only in until and retry conditions, which test the node's own attempt");
                break;
            case SubjectKind.Result when Part(subject) is { } part && part.Text is "stdout" or "stderr" or "exit"
                && node.Kind is not (NodeKind.Exec or NodeKind.Script) && node.Kind is not null:
                errors.Invalid("RF-13", part.Position, at,
                    $"{field}: 'result: {part.Text}' is valid only on an exec or script node; on this node, test result: error");
                break;
        }
    }

    /// <summary>CO-26: exactly one of exec and script; shell only with script.</summary>
    private static void Probe(Probe probe, PlanErrorSink errors)
    {
        var exec = probe.Key("exec");
        var script = probe.Key("script");
        if (exec is not null && script is not null)
        {
            var later = Later(exec.Position, script.Position) ? exec : script;
            errors.Invalid("CO-26", later.Position, probe.Node,
                "a probe runs exec: or script:, not both: keep one", "probe: { exec: [docker, info] }");
        }
        else if (exec is null && script is null && probe.Keys.Count > 0)
        {
            errors.Invalid("CO-26", probe.Position, probe.Node,
                "a probe needs exec: or script:, the command it runs", "probe: { exec: [docker, info] }");
        }

        if (probe.Key("shell") is { } shell && script is null)
        {
            errors.Invalid("CO-26", shell.Position, probe.Node,
                "a probe's shell: applies to script: only: an exec probe runs its program directly",
                "probe: { script: '…', shell: bash }");
        }
    }

    /// <summary>CO-47: an operator with a subject it doesn't take, located at the operator.</summary>
    private static void Pairing(ConditionSubject subject, ConditionOperator op, string at, PlanErrorSink errors)
    {
        var part = subject.Kind == SubjectKind.Result ? Part(subject)?.Text : null;
        if (OperatorTable.Takes(op.Kind, subject.Kind, part))
        {
            return;
        }

        var what = part is null ? $"'{subject.Key.Name}'" : $"'result: {part}'";
        errors.Invalid("CO-47", op.Key.Position, at,
            $"'{op.Key.Name}' doesn't apply to {what}: it takes {OperatorTable.Describe(op.Kind)}");
    }

    /// <summary>CO-12: <c>ignore_case</c> only with the string operators, located at the later key.</summary>
    private static void IgnoreCase(Condition condition, PlanErrorSink errors)
    {
        if (condition.Key("ignore_case") is not { } key)
        {
            return;
        }

        foreach (var op in condition.Operators)
        {
            if (OperatorTable.TakesIgnoreCase(op.Kind))
            {
                continue;
            }

            errors.Invalid("CO-12", Later(key.Position, op.Key.Position) ? key.Position : op.Key.Position, condition.Node,
                $"ignore_case: applies to equals, in, contains, matches and their not_ forms, not to '{op.Key.Name}': remove it");
        }
    }

    /// <summary>RF-14: <c>not:</c> around a simple condition whose operator has a <c>not_*</c> form, located at <c>not</c>.</summary>
    private static void NotAroundSimple(Condition condition, PlanErrorSink errors)
    {
        if (condition.Not is not { NodeKindKey: null } inner || condition.Key("not") is not { } key
            || inner.Subjects.Count != 1 || inner.Operators.Count != 1
            || inner.Keys.Any(k => k.Name is "all" or "any" or "not")
            || OperatorTable.Opposite(inner.Operators[0].Kind) is not { } opposite)
        {
            return;
        }

        var subject = inner.Subjects[0].Key.Name;
        errors.Invalid("RF-14", key.Position, condition.Node,
            $"not: around a single check: use the operator's own negation, {opposite}:",
            $"{{ {subject}: …, {opposite}: … }}");
    }

    private static PlanScalar? Part(ConditionSubject subject) => (subject.Value as LiteralValue)?.Scalar;

    /// <summary>Whether <paramref name="a"/> comes after <paramref name="b"/> in the document.</summary>
    private static bool Later(PlanPosition a, PlanPosition b) => a.Line > b.Line || (a.Line == b.Line && a.Column > b.Column);
}
