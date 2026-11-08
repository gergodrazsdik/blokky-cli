namespace Blokky.Core.Ast;

/// <summary>Traversals of the AST for the static rules and the feature derivation: linear, in document order.</summary>
public static class PlanWalk
{
    /// <summary>Every node, the root first, then depth-first in document order of the body fields.</summary>
    public static IEnumerable<PlanNode> Nodes(PlanAst plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var stack = new Stack<PlanNode>();
        stack.Push(plan.Root);
        while (stack.Count > 0)
        {
            var node = stack.Pop();
            yield return node;
            var children = node.Bodies().SelectMany(b => b.Nodes).ToList();
            for (var i = children.Count - 1; i >= 0; i--)
            {
                stack.Push(children[i]);
            }
        }
    }

    /// <summary>The conditions written directly on <paramref name="node"/>, with their field: <c>if</c>, <c>assert</c>,
    /// <c>when</c>, <c>while</c>, <c>until</c>, a <c>retry</c> condition and services' <c>ready</c>.</summary>
    public static IEnumerable<(string Field, Condition Condition)> Conditions(PlanNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        foreach (var key in node.Keys)
        {
            var condition = key.Name switch
            {
                "if" => node.If,
                "assert" => node.Assert,
                "when" => node.When,
                "while" => node.While,
                "until" => node.Until,
                "retry" => node.Retry?.Condition,
                _ => null,
            };
            if (condition is not null)
            {
                yield return (key.Name, condition);
            }
        }

        foreach (var service in node.Services ?? [])
        {
            if (service.Ready is { } ready)
            {
                yield return ("services.ready", ready);
            }
        }
    }

    /// <summary><paramref name="condition"/> and every condition nested in it (<c>all</c>, <c>any</c>, <c>not</c>), pre-order.</summary>
    public static IEnumerable<Condition> Within(Condition condition)
    {
        ArgumentNullException.ThrowIfNull(condition);
        var stack = new Stack<Condition>();
        stack.Push(condition);
        while (stack.Count > 0)
        {
            var c = stack.Pop();
            yield return c;
            if (c.Not is { } not)
            {
                stack.Push(not);
            }

            foreach (var sub in (c.Any ?? []).Reverse().Concat((c.All ?? []).Reverse()))
            {
                stack.Push(sub);
            }
        }
    }
}
