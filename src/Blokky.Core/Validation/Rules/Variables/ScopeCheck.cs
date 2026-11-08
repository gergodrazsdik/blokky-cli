using Blokky.Core.Ast;

namespace Blokky.Core.Validation.Rules.Variables;

/// <summary>
/// The syntactic scope check (variables.md §6, VA-38): walks the plan in execution order with the set of names that some
/// binding can provide at each point, and reports bindings of a bound name (RF-7, RF-26, RF-8) and references with no
/// possible binding (RF-6, RF-15, RF-16, RF-23, RF-27, in errors.md §4's precedence).
/// </summary>
/// <remarks>
/// The visible names live in one dictionary with an undo log, so branches, loop iterations and <c>finally</c> lists drop
/// their bindings without copying the scope. A binding published out of a nested construct is re-added once per enclosing
/// construct, so the work is linear in the plan times its nesting depth, which the reader caps (SY-9).
/// </remarks>
internal sealed class ScopeCheck
{
    private const int PlanScope = 0;

    private readonly PlanAst _plan;
    private readonly PlanErrorSink _errors;
    private readonly VariableSites _sites;
    private readonly Dictionary<string, Bound> _visible = new(StringComparer.Ordinal);
    private readonly List<Bound> _log = [];
    private readonly HashSet<string> _everBound = new(StringComparer.Ordinal);
    private readonly HashSet<string> _finallyBound = new(StringComparer.Ordinal);
    private readonly HashSet<string> _loopBound = new(StringComparer.Ordinal);
    private readonly List<(VariableReference Reference, Context Context, PlanNode? Node)> _unresolved = [];
    private int _scopes;

    private ScopeCheck(PlanAst plan, PlanErrorSink errors)
    {
        _plan = plan;
        _errors = errors;
        _sites = new VariableSites(plan);
    }

    private enum Context
    {
        Body,
        Modifier,
        Vars,
    }

    /// <summary>Checks <paramref name="plan"/>'s bindings and references, reporting into <paramref name="errors"/>.</summary>
    public static void Run(PlanAst plan, PlanErrorSink errors)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(errors);
        new ScopeCheck(plan, errors).Check();
    }

    private void Check()
    {
        // The plan scope: inputs, then vars in order (VA-7, VA-61).
        foreach (var input in _plan.Inputs ?? [])
        {
            if (input.NameValid)
            {
                Bind(new VariableBinding(input.Name.Name, BindingKind.Input, input.Name.Position, "/"), PlanScope);
            }
        }

        foreach (var entry in _plan.Vars ?? [])
        {
            Resolve(_sites.InValue(entry.Value, "/"), Context.Vars, null);
            if (entry.NameValid)
            {
                Bind(new VariableBinding(entry.Name.Name, BindingKind.Vars, entry.Name.Position, "/"), PlanScope);
            }
        }

        Visit(_plan.Root, NewScope());

        foreach (var output in _plan.Outputs ?? [])
        {
            if (_visible.ContainsKey(output.Text))
            {
                continue;
            }

            if (_loopBound.Contains(output.Text) && !_finallyBound.Contains(output.Text))
            {
                _errors.Invalid("RF-15", output.Position, "/",
                    $"outputs: '{output.Text}' is bound only inside a loop body, so it has no value after the loop",
                    "collect: { " + output.Text + ": LIST }");
            }
            else
            {
                Unresolved(new VariableReference(output.Text, output.Position, "/", ReferenceSite.Output), Context.Body, null);
            }
        }

        foreach (var (reference, context, node) in _unresolved)
        {
            Report(reference, context, node);
        }
    }

    // ---- nodes ----------------------------------------------------------------------------------------------------

    private void Visit(PlanNode node, int sequence)
    {
        // Modifiers outside repetition see only the enclosing scope (VA-37).
        Resolve(_sites.Modifiers(node), Context.Modifier, node);
        Resolve(VariableSites.ForSources(node), Context.Modifier, node);

        var introducedMark = _log.Count;
        var introduced = NewScope();
        var introducedAny = false;
        foreach (var binding in VariableSites.Introduced(node))
        {
            introducedAny |= Bind(binding, introduced) is not null;
        }

        var loop = node.Has("for") || node.Has("while") || node.Has("until");
        var body = loop ? NewScope() : sequence;
        var bodyMark = _log.Count;

        Resolve(_sites.Kind(node), Context.Body, node);
        foreach (var entry in node.Set ?? [])
        {
            Resolve(_sites.InValue(entry.Value, node.Path), Context.Body, node);
            if (entry.NameValid)
            {
                Bind(new VariableBinding(entry.Name.Name, BindingKind.Set, entry.Name.Position, node.Path), body);
            }
        }

        Children(node, body);

        // Own captures: per iteration under for, published otherwise (VA-32); they reach the node's own condition.
        var ownScope = node.Has("for") ? body : sequence;
        var captures = new List<Bound>();
        foreach (var binding in VariableSites.Captures(node))
        {
            if (Bind(binding, ownScope) is { } bound)
            {
                captures.Add(bound);
            }
        }

        Resolve(_sites.Repetition(node), Context.Body, node);

        if (node.Finally is { } final)
        {
            var mark = _log.Count;
            var scope = NewScope();
            foreach (var child in final)
            {
                Visit(child, scope);
            }

            _finallyBound.UnionWith(Undo(mark).Select(b => b.Binding.Name));
        }

        if (loop)
        {
            // Loop iterations never publish (VA-33, VA-34); a while or until node's own captures do (VA-32).
            foreach (var removed in Undo(bodyMark))
            {
                if (!captures.Contains(removed) || node.Has("for"))
                {
                    _loopBound.Add(removed.Binding.Name);
                }
            }

            if (!node.Has("for"))
            {
                captures.ForEach(Readd);
            }
        }

        foreach (var binding in VariableSites.Collected(node))
        {
            Bind(binding, sequence);
        }

        if (introducedAny)
        {
            foreach (var kept in Undo(introducedMark).Where(b => b.Scope != introduced))
            {
                Readd(kept);
            }
        }
    }

    private void Children(PlanNode node, int scope)
    {
        foreach (var key in node.Keys)
        {
            switch (key.Name)
            {
                case "do" when node.Do is { } list:
                    foreach (var child in list)
                    {
                        Visit(child, scope);
                    }

                    break;
                case "all" when node.All is { } list:
                    Parallel(list, scope);
                    break;
                case "any" when node.Any is { } list:
                    Alternatives(list.Select(c => (IReadOnlyList<PlanNode>)[c]), scope);
                    break;
                case "race" when node.Race is { } list:
                    Alternatives(list.Select(c => (IReadOnlyList<PlanNode>)[c]), scope);
                    break;
                case "then":
                    // then and else are alternatives (VA-31, VA-36); visited together at the first of them.
                    Alternatives(new[] { node.Then, node.Else }.OfType<IReadOnlyList<PlanNode>>(), scope);
                    break;
                case "else" when !node.Has("then") && node.Else is { } list:
                    Alternatives([list], scope);
                    break;
            }
        }
    }

    /// <summary>Alternatives: each starts from the same scope; afterwards, what any of them bound is possibly bound.</summary>
    private void Alternatives(IEnumerable<IReadOnlyList<PlanNode>> branches, int scope)
    {
        var mark = _log.Count;
        var published = new List<Bound>();
        foreach (var branch in branches)
        {
            foreach (var child in branch)
            {
                Visit(child, scope);
            }

            published.AddRange(Undo(mark));
        }

        published.ForEach(Readd);
    }

    /// <summary><c>all</c>: like alternatives, but two children don't bind the same name (RF-8, VA-29).</summary>
    private void Parallel(IReadOnlyList<PlanNode> children, int scope)
    {
        var mark = _log.Count;
        var published = new List<Bound>();
        var seen = new Dictionary<string, Bound>(StringComparer.Ordinal);
        foreach (var child in children)
        {
            Visit(child, scope);
            foreach (var bound in Undo(mark))
            {
                if (seen.TryGetValue(bound.Binding.Name, out var first))
                {
                    _errors.Invalid("RF-8", bound.Binding.Position, bound.Binding.Node,
                        $"'{bound.Binding.Name}' is also bound by another child of all: (line {first.Binding.Position.Line}); "
                        + "children of all run in parallel, so each binds its own names");
                    continue;
                }

                seen.Add(bound.Binding.Name, bound);
                published.Add(bound);
            }
        }

        published.ForEach(Readd);
    }

    // ---- bindings -------------------------------------------------------------------------------------------------

    private int NewScope() => ++_scopes;

    /// <summary>Binds a name in <paramref name="scope"/>, or reports RF-7 or RF-26 if it is already bound (VA-36).</summary>
    private Bound? Bind(VariableBinding binding, int scope)
    {
        _everBound.Add(binding.Name);
        if (_visible.TryGetValue(binding.Name, out var existing))
        {
            var where = $"line {existing.Binding.Position.Line}";
            if (existing.Binding.Kind == BindingKind.Input || existing.Scope != scope)
            {
                var what = existing.Binding.Kind == BindingKind.Input ? $"the input declared at {where}" : $"an enclosing scope ({where})";
                _errors.Invalid("RF-7", binding.Position, binding.Node,
                    $"'{binding.Name}' shadows {what}: a name is bound once; pick another name");
            }
            else
            {
                _errors.Invalid("RF-26", binding.Position, binding.Node,
                    $"'{binding.Name}' is already bound at {where}: a name is bound once; pick another name");
            }

            return null;
        }

        var bound = new Bound(binding, scope);
        _visible.Add(binding.Name, bound);
        _log.Add(bound);
        return bound;
    }

    private void Readd(Bound bound)
    {
        if (_visible.TryAdd(bound.Binding.Name, bound))
        {
            _log.Add(bound);
        }
    }

    /// <summary>Removes the bindings made since <paramref name="mark"/> and returns them, in order.</summary>
    private List<Bound> Undo(int mark)
    {
        var removed = _log.GetRange(mark, _log.Count - mark);
        _log.RemoveRange(mark, removed.Count);
        foreach (var bound in removed)
        {
            _visible.Remove(bound.Binding.Name);
        }

        return removed;
    }

    // ---- references -----------------------------------------------------------------------------------------------

    private void Resolve(IEnumerable<VariableReference> references, Context context, PlanNode? node)
    {
        foreach (var reference in references)
        {
            if (!_visible.ContainsKey(reference.Name))
            {
                Unresolved(reference, context, node);
            }
        }
    }

    private void Unresolved(VariableReference reference, Context context, PlanNode? node) =>
        _unresolved.Add((reference, context, node));

    /// <summary>The most specific rule for a reference no binding can provide (errors.md §4: RF-6, RF-16, RF-27 over RF-23).</summary>
    private void Report(VariableReference reference, Context context, PlanNode? node)
    {
        var name = reference.Name;
        var shown = reference.Site == ReferenceSite.Placeholder ? "${" + name + "}" : $"'{name}'";
        if (context == Context.Modifier && node is not null && VariableSites.Own(node).Contains(name, StringComparer.Ordinal))
        {
            _errors.Invalid("RF-6", reference.Position, reference.Node,
                $"{shown} is bound by this node itself, so its modifiers can't use it: move it into the body");
        }
        else if (context == Context.Vars && _everBound.Contains(name))
        {
            _errors.Invalid("RF-27", reference.Position, reference.Node,
                $"{shown}: vars values use only inputs, earlier vars entries and from_env, never a later entry or a value "
                + "produced by running something");
        }
        else if (context != Context.Vars && _finallyBound.Contains(name))
        {
            _errors.Invalid("RF-16", reference.Position, reference.Node,
                $"{shown} is captured under finally: and visible only to later nodes of that finally: list");
        }
        else
        {
            _errors.Invalid("RF-23", reference.Position, reference.Node,
                $"{shown} names no variable that is bound before it: check the name, and that it is bound earlier on this path "
                + "(environment variables are read with { from_env: NAME })");
        }
    }

    /// <summary>A visible binding and the scope it was bound in.</summary>
    private sealed record Bound(VariableBinding Binding, int Scope);
}
