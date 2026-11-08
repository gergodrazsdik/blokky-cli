using Blokky.Core.Ast;

namespace Blokky.Core.Validation.Rules.Reference;

/// <summary>
/// The nodes of a plan in pre-order, with each node's parent, the body field it sits in and the end of its subtree, so the
/// reference rules can relate a node to its ancestors and descendants in linear time. Built once per rule from the AST.
/// </summary>
internal sealed class PlanTree
{
    private PlanTree(List<PlanNode> nodes, List<int> parents, List<string?> fields, int[] ends)
    {
        Nodes = nodes;
        Parents = parents;
        Fields = fields;
        Ends = ends;
    }

    /// <summary>Every node, the root first, then depth-first in document order of the body fields.</summary>
    public IReadOnlyList<PlanNode> Nodes { get; }

    /// <summary>The index of each node's parent; -1 for the root.</summary>
    public IReadOnlyList<int> Parents { get; }

    /// <summary>The body field each node sits in (<c>do</c>, <c>then</c>, <c>finally</c> …); null for the root.</summary>
    public IReadOnlyList<string?> Fields { get; }

    /// <summary>The end (exclusive) of each node's subtree: its descendants are the indexes between it and the end.</summary>
    public IReadOnlyList<int> Ends { get; }

    /// <summary>Indexes the nodes of <paramref name="plan"/>.</summary>
    public static PlanTree Build(PlanAst plan)
    {
        var nodes = new List<PlanNode>();
        var parents = new List<int>();
        var fields = new List<string?>();
        var stack = new Stack<(PlanNode Node, int Parent, string? Field)>();
        stack.Push((plan.Root, -1, null));
        while (stack.Count > 0)
        {
            var (node, parent, field) = stack.Pop();
            var index = nodes.Count;
            nodes.Add(node);
            parents.Add(parent);
            fields.Add(field);
            var children = new List<(PlanNode, string)>();
            foreach (var (name, list) in node.Bodies())
            {
                children.AddRange(list.Select(child => (child, name)));
            }

            for (var i = children.Count - 1; i >= 0; i--)
            {
                stack.Push((children[i].Item1, index, children[i].Item2));
            }
        }

        // In pre-order a subtree is contiguous: it ends where the next node outside it starts, which is found bottom-up.
        var ends = new int[nodes.Count];
        for (var i = nodes.Count - 1; i >= 0; i--)
        {
            ends[i] = Math.Max(ends[i], i + 1);
            if (parents[i] >= 0)
            {
                ends[parents[i]] = Math.Max(ends[parents[i]], ends[i]);
            }
        }

        return new PlanTree(nodes, parents, fields, ends);
    }
}
