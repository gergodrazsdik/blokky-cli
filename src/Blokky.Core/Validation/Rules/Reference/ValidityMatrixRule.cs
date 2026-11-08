using Blokky.Core.Ast;

namespace Blokky.Core.Validation.Rules.Reference;

/// <summary>
/// Which keys a node may carry (reference.md §7 and §5.2): the validity matrix (RF-3) and its more specific rules, which win
/// over it (errors.md §4): one repetition modifier (RF-2), <c>attempt_timeout</c> with repetition (RF-4), bounded
/// <c>while</c>/<c>until</c> (RF-5), captures on leaves only (RF-12), no-process kinds (RF-28), where <c>index</c>,
/// <c>carry</c> and <c>collect</c> go (RF-30), <c>concurrency</c>/<c>fail_fast</c> beside <c>for</c> (RF-31), companion
/// fields (RF-32) and <c>ignore_errors</c> on the root (RF-33). Each error is at the offending key.
/// </summary>
public sealed class ValidityMatrixRule : IPlanRule
{
    /// <inheritdoc/>
    public IReadOnlyList<string> Rules { get; } = ["RF-2", "RF-3", "RF-4", "RF-5", "RF-12", "RF-28", "RF-30", "RF-31", "RF-32", "RF-33"];

    /// <inheritdoc/>
    public void Check(PlanContext context, PlanErrorSink errors)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(errors);
        foreach (var node in PlanWalk.Nodes(context.Ast))
        {
            if (node.Kind is { } kind)
            {
                CheckNode(node, kind, node == context.Ast.Root, errors);
            }
        }
    }

    private static void CheckNode(PlanNode node, NodeKind kind, bool root, PlanErrorSink errors)
    {
        var kindName = KindTables.Name(kind);
        string? repetition = null;
        foreach (var key in node.Keys)
        {
            if (Array.IndexOf(KindTables.Repetitions, key.Name) < 0)
            {
                continue;
            }

            if (repetition is null)
            {
                repetition = key.Name;
            }
            else
            {
                errors.Invalid("RF-2", key.Position, node.Path,
                    $"a node has at most one repetition modifier, and this one already has {repetition}: nest the nodes instead",
                    $"move {key.Name}: to a node inside it");
            }
        }

        foreach (var key in node.Keys)
        {
            var at = key.Position;
            var name = key.Name;
            switch (name)
            {
                case "timeout" or "finally" or "ignore_errors" when KindTables.NoProcess(kind):
                    errors.Invalid("RF-28", at, node.Path,
                        $"{name}: isn't valid on a {kindName} node, which starts no process", $"remove {name}:");
                    break;
                case "ignore_errors" when root:
                    errors.Invalid("RF-33", at, node.Path,
                        "ignore_errors: isn't valid on the root: there is no parent to hide the failure from", "remove ignore_errors:");
                    break;
                case "finally_timeout" when KindTables.NoProcess(kind):
                    errors.Invalid("RF-28", at, node.Path,
                        $"finally_timeout: isn't valid on a {kindName} node, which starts no process", "remove finally_timeout:");
                    break;
                case "finally_timeout" when !node.Has("finally"):
                    errors.Invalid("RF-32", at, node.Path, "finally_timeout: is valid only together with finally:",
                        "remove finally_timeout: or add finally:");
                    break;
                case "retry" or "while" or "until" when !KindTables.TakesScope(kind):
                case "for" when !KindTables.TakesFor(kind):
                    errors.Invalid("RF-3", at, node.Path, $"{name}: isn't valid on a {kindName} node (reference.md §7)",
                        $"put the {kindName} in a do node that repeats");
                    break;
                case "attempt_timeout":
                    AttemptTimeout(node, kind, repetition, key, errors);
                    break;
                case "concurrency" or "fail_fast":
                    GroupSetting(node, kind, key, errors);
                    break;
                default:
                    if (Array.IndexOf(KindTables.Scope, name) >= 0 && !KindTables.TakesScope(kind))
                    {
                        errors.Invalid("RF-3", at, node.Path, $"{name}: isn't valid on a {kindName} node (reference.md §7)",
                            $"remove {name}: or put the {kindName} in a do node that has it");
                    }
                    else if (Array.IndexOf(KindTables.LeafOptions, name) >= 0 && !KindTables.IsLeaf(kind))
                    {
                        errors.Invalid("RF-3", at, node.Path, $"{name}: is a leaf option, valid only on exec and script nodes",
                            $"remove {name}:");
                    }
                    else if (Array.IndexOf(KindTables.Captures, name) >= 0 && !KindTables.IsLeaf(kind))
                    {
                        errors.Invalid("RF-12", at, node.Path, $"{name}: is valid only on exec and script nodes",
                            "capture on the leaf inside the group instead");
                    }
                    else if (KindTables.CompanionOf(name) is { } owners && Array.IndexOf(owners, kind) < 0)
                    {
                        var list = string.Join(" or ", owners.Select(KindTables.Name));
                        errors.Invalid("RF-32", at, node.Path, $"{name}: belongs to {list} nodes, not to a {kindName} node",
                            $"remove {name}:");
                    }
                    else if (KindTables.SiblingOf(name) is { } with)
                    {
                        Sibling(node, repetition, key, with, errors);
                    }

                    break;
            }
        }

        if (kind == NodeKind.If && !node.Has("then") && node.KindKey is { } ifKey)
        {
            errors.Invalid("RF-32", ifKey.Position, node.Path, "an if node needs then:", "add then: with the nodes to run");
        }

        foreach (var name in (string[])["while", "until"])
        {
            if (node.Key(name) is { } loop && !node.Has("max") && !node.Has("timeout"))
            {
                errors.Invalid("RF-5", loop.Position, node.Path, $"a {name} loop needs max:, timeout:, or both: it would be unbounded",
                    "add max: or timeout:");
            }
        }
    }

    private static void AttemptTimeout(PlanNode node, NodeKind kind, string? repetition, PlanKey key, PlanErrorSink errors)
    {
        if (repetition is null)
        {
            errors.Invalid("RF-4", key.Position, node.Path, "attempt_timeout: is valid only with retry, while, until or for",
                "use timeout: for a limit on the whole node");
        }
        else if (!KindTables.TakesScope(kind))
        {
            errors.Invalid("RF-3", key.Position, node.Path,
                $"attempt_timeout: isn't valid on a {KindTables.Name(kind)} node (reference.md §7)", "remove attempt_timeout:");
        }
    }

    private static void GroupSetting(PlanNode node, NodeKind kind, PlanKey key, PlanErrorSink errors)
    {
        var name = key.Name;
        if (node.Has("for"))
        {
            errors.Invalid("RF-31", key.Position, node.Path,
                $"{name}: beside for: would apply to the inner {KindTables.Name(kind)}, not to the loop",
                $"move {name}: into the for: mapping, or the group into a child node");
            return;
        }

        var valid = name == "concurrency" ? kind is NodeKind.All or NodeKind.Any : kind is NodeKind.Do or NodeKind.All;
        if (!valid)
        {
            errors.Invalid("RF-3", key.Position, node.Path, $"{name}: isn't valid on a {KindTables.Name(kind)} node (reference.md §7)",
                KindTables.TakesFor(kind) ? $"inside a loop, put {name}: in the for: mapping" : $"remove {name}:");
        }
    }

    private static void Sibling(PlanNode node, string? repetition, PlanKey key, string[] with, PlanErrorSink errors)
    {
        if (repetition is not null && Array.IndexOf(with, repetition) >= 0)
        {
            return;
        }

        var rule = key.Name is "index" or "carry" or "collect" ? "RF-30" : "RF-3";
        var valid = string.Join(", ", with);
        var message = repetition is null
            ? $"{key.Name}: is valid only with {valid}, and this node doesn't repeat"
            : $"{key.Name}: isn't valid with {repetition}: only with {valid} (reference.md §5.2)";
        var fix = key.Name == "index" && repetition == "for" ? "use the for loop's <AS>_INDEX instead" : $"remove {key.Name}:";
        errors.Invalid(rule, key.Position, node.Path, message, fix);
    }
}
