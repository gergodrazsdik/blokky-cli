using Blokky.Core.Ast;

namespace Blokky.Core.Validation.Rules.Reference;

/// <summary>
/// Rules on modifier values: an absolute <c>cwd</c> isn't combined with <c>tempdir</c> (RF-11, reference.md §4.5; absolute
/// by the host OS's rules, CP-41), service names are present and unique within a node (RF-24, §4.9), and a mount's
/// <c>path</c> is an absolute container path (CP-13, containers.md).
/// </summary>
public sealed class ModifierValueRule : IPlanRule
{
    /// <inheritdoc/>
    public IReadOnlyList<string> Rules { get; } = ["RF-11", "RF-24", "CP-13"];

    /// <inheritdoc/>
    public void Check(PlanContext context, PlanErrorSink errors)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(errors);
        foreach (var node in PlanWalk.Nodes(context.Ast))
        {
            if (node.Cwd is LiteralValue cwd && node.Key("tempdir") is { } tempdirKey && node.Key("cwd") is { } cwdKey
                && Path.IsPathFullyQualified(cwd.Scalar.Text))
            {
                errors.Invalid("RF-11", Positions.Later(cwdKey, tempdirKey).Position, node.Path,
                    "an absolute cwd: can't be combined with tempdir:, which becomes the node's cwd",
                    "make cwd: relative (it resolves inside the tempdir), or drop tempdir:");
            }

            Services(node, errors);
            foreach (var mount in node.Container?.Mounts ?? [])
            {
                if (mount.Path is { } path && !path.Text.StartsWith('/'))
                {
                    errors.Value("CP-13", path.Position, node.Path,
                        $"a mount's path is an absolute container path, starting with /: '{path.Text}' isn't",
                        $"write it as /{path.Text}");
                }
            }
        }
    }

    private static void Services(PlanNode node, PlanErrorSink errors)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var service in node.Services ?? [])
        {
            if (!service.Has("name"))
            {
                errors.Invalid("RF-24", service.Position, node.Path, "a service needs name:, unique among the node's services",
                    "add name:");
            }
            else if (service.Name is { } name && !names.Add(name.Text))
            {
                errors.Invalid("RF-24", name.Position, node.Path, $"two services of this node are named '{name.Text}'",
                    "give each service its own name");
            }
        }
    }
}
