using Blokky.Core.Ast;

namespace Blokky.Core.Validation.Rules.Reference;

/// <summary>
/// Rules about where a node sits: no <c>lock</c> inside a <c>lock</c> (RF-17, reference.md §4.4), no <c>container</c> in
/// another container's scope (CP-19), and a <c>return</c> with an enclosing sequence it doesn't reach through an
/// <c>all</c>, <c>any</c> or <c>race</c> (RF-29, §2.13). <c>finally</c> lists count as descendants.
/// </summary>
public sealed class NestingRule : IPlanRule
{
    private enum Reach
    {
        /// <summary>No sequence encloses the node.</summary>
        None,

        /// <summary>The nearest sequence is a <c>do</c> or <c>finally</c> list.</summary>
        Sequence,

        /// <summary>The nearest sequence is reached through an <c>all</c>, <c>any</c> or <c>race</c>.</summary>
        Group,
    }

    /// <inheritdoc/>
    public IReadOnlyList<string> Rules { get; } = ["RF-17", "CP-19", "RF-29"];

    /// <inheritdoc/>
    public void Check(PlanContext context, PlanErrorSink errors)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(errors);
        var tree = PlanTree.Build(context.Ast);
        var count = tree.Nodes.Count;
        var inLock = new bool[count];
        var inContainer = new bool[count];
        var reach = new Reach[count];
        for (var i = 0; i < count; i++)
        {
            var node = tree.Nodes[i];
            var parent = tree.Parents[i];
            if (parent >= 0)
            {
                var up = tree.Nodes[parent];
                inLock[i] = inLock[parent] || up.Has("lock");
                inContainer[i] = inContainer[parent] || up.Has("container");
                reach[i] = tree.Fields[i] switch
                {
                    "do" or "finally" => Reach.Sequence,
                    "then" or "else" => reach[parent],
                    _ => Reach.Group,
                };
            }

            if (inLock[i] && node.Key("lock") is { } lockKey)
            {
                errors.Invalid("RF-17", lockKey.Position, node.Path,
                    "a lock inside a node that already holds a lock isn't allowed (locks don't nest)",
                    "remove the inner lock, or take one lock around both");
            }

            if (inContainer[i] && node.Key("container") is { } containerKey)
            {
                errors.Invalid("CP-19", containerKey.Position, node.Path,
                    "a container inside another container's scope isn't allowed: this node already runs in that container",
                    "remove the inner container, or move the node out of the outer one");
            }

            if (node.Kind == NodeKind.Return && node.KindKey is { } returnKey && reach[i] != Reach.Sequence)
            {
                var message = reach[i] == Reach.Group
                    ? "return: would end a sequence through an all, any or race, which has no order to end early"
                    : "return: has no enclosing sequence to end";
                errors.Invalid("RF-29", returnKey.Position, node.Path, message, "put the return in a do list");
            }
        }
    }
}
