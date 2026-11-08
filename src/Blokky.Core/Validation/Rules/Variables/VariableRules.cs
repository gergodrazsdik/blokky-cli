using System.Text.Json.Nodes;
using Blokky.Core.Ast;
using Blokky.Core.Errors;
using Blokky.Core.Reading;
using Blokky.Core.Syntax;

namespace Blokky.Core.Validation.Rules.Variables;

/// <summary>
/// The variables and interpolation rule family (T-0083; variables.md, syntax.md §7, reference.md §8): references and
/// <c>${</c> in literal fields (RF-9), secret literals (RF-19), environment names (RF-10), an unset <c>from_env</c> in
/// <c>vars</c> (<c>BK_FROM_ENV_UNSET</c>, VA-40), and the syntactic scope check (<see cref="ScopeCheck"/>: RF-6, RF-7,
/// RF-8, RF-15, RF-16, RF-23, RF-26, RF-27).
/// </summary>
public sealed class VariableRules : IPlanRule
{
    /// <inheritdoc/>
    public IReadOnlyList<string> Rules { get; } =
        ["RF-6", "RF-7", "RF-8", "RF-9", "RF-10", "RF-15", "RF-16", "RF-19", "RF-23", "RF-26", "RF-27", "VA-40"];

    /// <inheritdoc/>
    public void Check(PlanContext context, PlanErrorSink errors)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(errors);
        var plan = context.Ast;
        Misplaced(plan, errors);
        LiteralText(plan, errors);
        foreach (var node in PlanWalk.Nodes(plan))
        {
            Environment(node, errors);
        }

        FromEnv(plan, context.EnvironmentVariable ?? System.Environment.GetEnvironmentVariable, errors);
        ScopeCheck.Run(plan, errors);
    }

    /// <summary>RF-9 for a reference in a field that doesn't take one; RF-19 for a secret literal (SY-91), at its secret key.</summary>
    private static void Misplaced(PlanAst plan, PlanErrorSink errors)
    {
        foreach (var misplaced in plan.MisplacedReferences)
        {
            if (misplaced.Value is SecretLiteralValue secret)
            {
                errors.Invalid("RF-19", secret.SecretKey, misplaced.Node,
                    "a literal can't be marked secret: secrets come only by reference", "{ from_env: NAME, secret: true }");
            }
            else
            {
                errors.Invalid("RF-9", misplaced.Value.Position, misplaced.Node,
                    $"{misplaced.Field}: takes a literal value, never a variable"
                    + (misplaced.Field.StartsWith("exec", StringComparison.Ordinal) ? " (argv[0] is the program, always a literal)" : ""));
            }
        }
    }

    /// <summary>RF-9 (SY-76, SY-91) for a <c>${</c> in a literal field; <c>$${</c> is a literal <c>${</c> (SY-84).</summary>
    private static void LiteralText(PlanAst plan, PlanErrorSink errors)
    {
        foreach (var scalar in plan.Scalars)
        {
            if (InterpolationSyntax.IsLiteralText(scalar) && InterpolationSyntax.HasPlaceholder(scalar.Text))
            {
                errors.Invalid("RF-9", scalar.Position, scalar.Node,
                    $"{scalar.Field}: is a literal field, so ${{…}} isn't interpolated here: build the string with a set template "
                    + "and pass it whole as { var: X }, or write $${ for a literal ${",
                    "set: { ARG: '--name=${X}' }  # then { var: ARG }");
            }
        }
    }

    /// <summary>RF-10 on a node, its services and its probes' <c>env</c>, <c>export</c> and <c>container.env_pass</c>.</summary>
    private static void Environment(PlanNode node, PlanErrorSink errors)
    {
        EnvAndExport(node.Env, node.Export, node.Container?.EnvPass, node.Path, errors);
        foreach (var service in node.Services ?? [])
        {
            EnvAndExport(service.Env, service.Export, null, node.Path, errors);
        }

        foreach (var (_, condition) in PlanWalk.Conditions(node))
        {
            foreach (var c in PlanWalk.Within(condition))
            {
                foreach (var subject in c.Subjects)
                {
                    if (subject.Probe is { } probe)
                    {
                        EnvAndExport(probe.Env, probe.Export, null, node.Path, errors);
                    }
                }
            }
        }
    }

    private static void EnvAndExport(IReadOnlyList<NamedValue>? env, IReadOnlyList<PlanScalar>? export,
        IReadOnlyList<PlanScalar>? envPass, string node, PlanErrorSink errors)
    {
        var set = new Dictionary<string, PlanPosition>(EnvironmentNames.NameComparer);
        foreach (var entry in env ?? [])
        {
            set.TryAdd(entry.Name.Name, entry.Name.Position);
            if (entry.NameValid && EnvironmentNames.IsForbidden(entry.Name.Name))
            {
                errors.Invalid("RF-10", entry.Name.Position, node,
                    $"env: can't set {entry.Name.Name}: it changes how shells and programs start (VA-41)");
            }
        }

        foreach (var name in export ?? [])
        {
            if (EnvironmentNames.IsReserved(name.Text))
            {
                errors.Invalid("RF-10", name.Position, node,
                    $"export: {name.Text} is a reserved environment name; to set it deliberately, use an explicit env: entry");
            }
            else if (set.TryGetValue(name.Text, out var at))
            {
                errors.Invalid("RF-10", Positions.Later(at, name.Position), node,
                    $"{name.Text} is both in env: and in export: on this node: keep one");
            }

            set.TryAdd(name.Text, name.Position);
        }

        foreach (var name in envPass ?? [])
        {
            if (EnvironmentNames.IsForbidden(name.Text) || EnvironmentNames.IsReserved(name.Text))
            {
                errors.Invalid("RF-10", name.Position, node,
                    $"container.env_pass: {name.Text} is a forbidden or reserved environment name and can't be passed (CP-42)");
            }
            else if (set.TryGetValue(name.Text, out var at))
            {
                errors.Invalid("RF-10", Positions.Later(at, name.Position), node,
                    $"{name.Text} is in container.env_pass and also in env: or export: on this node: keep one (CP-42)");
            }
        }
    }

    /// <summary>
    /// <c>BK_FROM_ENV_UNSET</c> for an unset <c>from_env</c> in <c>vars</c>, which binds before anything runs (VA-40, VA-58),
    /// at the environment variable name (SY-1). Elsewhere an unset <c>from_env</c> fails the node that uses it, at run time.
    /// </summary>
    private static void FromEnv(PlanAst plan, Func<string, string?> environment, PlanErrorSink errors)
    {
        foreach (var entry in plan.Vars ?? [])
        {
            if (entry.Value is ReferenceValue { Kind: ReferenceKind.FromEnv, Name: { } name } && environment(name.Text) is null)
            {
                errors.Add(new BlokkyError
                {
                    Code = ErrorCodes.FromEnvUnset,
                    Message = $"vars: {entry.Name.Name} reads the environment variable {name.Text}, which isn't set",
                    Node = "/",
                    Pos = name.Position.ToString(),
                    Detail = new JsonObject { ["name"] = name.Text },
                });
            }
        }
    }
}
