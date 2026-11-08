using Blokky.Core.Ast;
using Blokky.Core.Reading;
using Blokky.Core.Syntax;

namespace Blokky.Core.Validation.Rules.Variables;

/// <summary>Where a plan variable is referenced (RF-23's list, plus the <c>for</c> sources).</summary>
public enum ReferenceSite
{
    /// <summary><c>${X}</c> in a display string or template.</summary>
    Placeholder,

    /// <summary><c>{ var: X }</c>.</summary>
    VarValue,

    /// <summary>The <c>var:</c> condition subject.</summary>
    VarSubject,

    /// <summary><c>for.items.var</c> or <c>for.lines.var</c>.</summary>
    ForSource,

    /// <summary>An <c>outputs</c> entry.</summary>
    Output,
}

/// <summary>A reference to a plan variable.</summary>
/// <param name="Name">The variable.</param>
/// <param name="Position">
/// Where an error about it is located (SY-1): the name inside <c>{ var: X }</c> or a subject, the start of the string for
/// <c>${X}</c>.
/// </param>
/// <param name="Node">The path id of the node.</param>
/// <param name="Site">How it is written.</param>
public sealed record VariableReference(string Name, PlanPosition Position, string Node, ReferenceSite Site);

/// <summary>What binds a plan variable (variables.md §3).</summary>
public enum BindingKind
{
    /// <summary><c>inputs:</c>.</summary>
    Input,

    /// <summary><c>vars:</c>.</summary>
    Vars,

    /// <summary>A <c>set</c> entry.</summary>
    Set,

    /// <summary><c>capture: NAME</c> or a field of <c>capture: { NAME: .path }</c>.</summary>
    Capture,

    /// <summary><c>capture_file</c>.</summary>
    CaptureFile,

    /// <summary><c>for.as</c>.</summary>
    LoopItem,

    /// <summary><c>&lt;AS&gt;_INDEX</c> or <c>MATRIX_INDEX</c> (VA-5).</summary>
    LoopIndex,

    /// <summary>A <c>for.matrix</c> key.</summary>
    MatrixKey,

    /// <summary>A <c>for.fields</c> entry.</summary>
    ItemField,

    /// <summary><c>tempdir.as</c>.</summary>
    Tempdir,

    /// <summary><c>index</c> on <c>retry</c>, <c>while</c>, <c>until</c>.</summary>
    Index,

    /// <summary>A <c>carry</c> entry.</summary>
    Carry,

    /// <summary>A <c>collect</c> list.</summary>
    Collect,
}

/// <summary>A binding of a plan variable.</summary>
/// <param name="Name">The variable.</param>
/// <param name="Kind">What binds it.</param>
/// <param name="Position">Where its name is written (a derived name: the key it derives from).</param>
/// <param name="Node">The path id of the node that binds it (<c>/</c> for inputs and <c>vars</c>).</param>
public sealed record VariableBinding(string Name, BindingKind Kind, PlanPosition Position, string Node);

/// <summary>
/// The reference and binding sites of a plan, grouped by the scope they are evaluated in (variables.md §6). References
/// recorded as misplaced by the AST builder (<see cref="PlanAst.MisplacedReferences"/>) are RF-9's or RF-19's alone and
/// never listed. Names that failed their grammar are left out (the builder reported SY-52).
/// </summary>
public sealed class VariableSites
{
    private readonly HashSet<PlanValue> _misplaced;

    /// <summary>The sites of <paramref name="plan"/>.</summary>
    public VariableSites(PlanAst plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        _misplaced = new HashSet<PlanValue>(plan.MisplacedReferences.Select(m => m.Value), ReferenceEqualityComparer.Instance);
    }

    /// <summary>
    /// The references of the modifiers outside repetition (RF-6's list: <c>when</c>, <c>cwd</c>, <c>env</c>,
    /// <c>container</c>, <c>services</c>; <c>timeout</c>, <c>lock</c> and <c>tempdir</c> are literal): evaluated in the
    /// enclosing scope, before anything the node binds.
    /// </summary>
    public IEnumerable<VariableReference> Modifiers(PlanNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        var path = node.Path;
        var refs = Enumerable.Empty<VariableReference>();
        if (node.When is { } when)
        {
            refs = refs.Concat(InCondition(when, path));
        }

        refs = refs.Concat(InValue(node.Cwd, path)).Concat(InEnv(node.Env, path));
        foreach (var mount in node.Container?.Mounts ?? [])
        {
            refs = refs.Concat(InValue(mount.Host, path));
        }

        foreach (var service in node.Services ?? [])
        {
            refs = refs.Concat(InExec(service.Exec, path)).Concat(InEnv(service.Env, path)).Concat(InValue(service.Cwd, path));
            if (service.Ready is { } ready)
            {
                refs = refs.Concat(InCondition(ready, path));
            }
        }

        return refs;
    }

    /// <summary><c>for.items.var</c> and <c>for.lines.var</c>: the loop's source, read in the enclosing scope.</summary>
    public static IEnumerable<VariableReference> ForSources(PlanNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        foreach (var source in new[] { node.For?.ItemsVar, node.For?.LinesVar })
        {
            if (source is not null)
            {
                yield return new VariableReference(source.Text, source.Position, node.Path, ReferenceSite.ForSource);
            }
        }
    }

    /// <summary>
    /// The references of the node's kind and leaf options, in the body's scope (with the variables the node introduces):
    /// <c>name</c>, <c>exec</c> elements, <c>stdin</c>, the messages of <c>log</c>, <c>fail</c>, <c>return</c> and
    /// <c>assert</c>, and the conditions of <c>if</c> and <c>assert</c>. <c>set</c> is <see cref="InValue"/> per entry, in
    /// order (VA-60).
    /// </summary>
    public IEnumerable<VariableReference> Kind(PlanNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        var path = node.Path;
        var refs = InScalar(node.Name).Concat(InExec(node.Exec, path)).Concat(InValue(node.Stdin, path))
            .Concat(InScalar(node.Log)).Concat(InScalar(node.Fail)).Concat(InScalar(node.Return)).Concat(InScalar(node.Message));
        if (node.If is { } condition)
        {
            refs = refs.Concat(InCondition(condition, path));
        }

        if (node.Assert is { } assert)
        {
            refs = refs.Concat(InCondition(assert, path));
        }

        return refs;
    }

    /// <summary>
    /// The references of the repetition conditions (<c>while</c>, <c>until</c>, a <c>retry</c> condition), which see the
    /// node's own captures and its body's bindings (VA-32, VA-33).
    /// </summary>
    public IEnumerable<VariableReference> Repetition(PlanNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        foreach (var condition in new[] { node.While, node.Until, node.Retry?.Condition })
        {
            if (condition is not null)
            {
                foreach (var r in InCondition(condition, node.Path))
                {
                    yield return r;
                }
            }
        }
    }

    /// <summary>The references of a value: <c>{ var: X }</c> (also inside <c>{ file: … }</c>), or <c>${X}</c> in a template.</summary>
    public IEnumerable<VariableReference> InValue(PlanValue? value, string node)
    {
        switch (value)
        {
            case null:
                break;
            case var v when _misplaced.Contains(v):
                break;
            case ReferenceValue { Kind: ReferenceKind.Var, Name: { } name }:
                yield return new VariableReference(name.Text, name.Position, node, ReferenceSite.VarValue);
                break;
            case FileValue file:
                foreach (var r in InValue(file.Path, node))
                {
                    yield return r;
                }

                break;
            case LiteralValue literal:
                foreach (var r in InScalar(literal.Scalar))
                {
                    yield return r;
                }

                break;
        }
    }

    /// <summary>The <c>${X}</c> references of a display string or template; none for any other field.</summary>
    public static IEnumerable<VariableReference> InScalar(PlanScalar? scalar)
    {
        if (scalar is null || !InterpolationSyntax.Interpolates(scalar))
        {
            yield break;
        }

        foreach (var placeholder in InterpolationSyntax.Parse(scalar.Text))
        {
            if (placeholder.IsVariable)
            {
                yield return new VariableReference(placeholder.Content, scalar.Position, scalar.Node, ReferenceSite.Placeholder);
            }
        }
    }

    /// <summary>The references of a condition and the conditions nested in it: <c>var:</c> subjects, <c>path:</c> values, probes.</summary>
    public IEnumerable<VariableReference> InCondition(Condition condition, string node)
    {
        ArgumentNullException.ThrowIfNull(condition);
        foreach (var c in PlanWalk.Within(condition))
        {
            foreach (var subject in c.Subjects)
            {
                switch (subject.Kind)
                {
                    case SubjectKind.Var when subject.Value is LiteralValue { Scalar: var name }:
                        yield return new VariableReference(name.Text, name.Position, node, ReferenceSite.VarSubject);
                        break;
                    case SubjectKind.Path:
                        foreach (var r in InValue(subject.Value, node))
                        {
                            yield return r;
                        }

                        break;
                    case SubjectKind.Probe when subject.Probe is { } probe:
                        foreach (var r in InExec(probe.Exec, node).Concat(InEnv(probe.Env, node)))
                        {
                            yield return r;
                        }

                        break;
                }
            }
        }
    }

    /// <summary>The variables a node introduces for its body and its own condition, not published (VA-5, VA-37).</summary>
    public static IEnumerable<VariableBinding> Introduced(PlanNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        var path = node.Path;
        if (node.For is { } loop)
        {
            if (loop.As is { } item)
            {
                yield return new VariableBinding(item.Text, BindingKind.LoopItem, item.Position, path);
                yield return new VariableBinding(item.Text + "_INDEX", BindingKind.LoopIndex, item.Position, path);
            }

            if (loop.Matrix is { } matrix)
            {
                foreach (var (key, _) in matrix)
                {
                    if (Grammars.VariableName(key.Name) is null)
                    {
                        yield return new VariableBinding(key.Name, BindingKind.MatrixKey, key.Position, path);
                    }
                }

                yield return new VariableBinding("MATRIX_INDEX", BindingKind.LoopIndex, node.Key("for")?.Position ?? loop.Position, path);
            }

            foreach (var field in loop.Fields ?? [])
            {
                if (field.NameValid)
                {
                    yield return new VariableBinding(field.Name.Name, BindingKind.ItemField, field.Name.Position, path);
                }
            }
        }

        if (node.Tempdir?.As is { } tempdir)
        {
            yield return new VariableBinding(tempdir.Text, BindingKind.Tempdir, tempdir.Position, path);
        }

        if (node.Index?.As is { } index)
        {
            yield return new VariableBinding(index.Text, BindingKind.Index, index.Position, path);
        }

        foreach (var carry in node.Carry ?? [])
        {
            if (carry.NameValid)
            {
                yield return new VariableBinding(carry.Name.Name, BindingKind.Carry, carry.Name.Position, path);
            }
        }
    }

    /// <summary>The node's own captures: <c>capture</c> (both forms) and <c>capture_file</c>.</summary>
    public static IEnumerable<VariableBinding> Captures(PlanNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        if (node.Capture?.Name is { } name)
        {
            yield return new VariableBinding(name.Text, BindingKind.Capture, name.Position, node.Path);
        }

        foreach (var field in node.Capture?.Fields ?? [])
        {
            if (field.NameValid)
            {
                yield return new VariableBinding(field.Name.Name, BindingKind.Capture, field.Name.Position, node.Path);
            }
        }

        if (node.CaptureFile is { } file)
        {
            yield return new VariableBinding(file.Text, BindingKind.CaptureFile, file.Position, node.Path);
        }
    }

    /// <summary>The <c>collect</c> lists, bound after the loop in the enclosing scope (VA-55).</summary>
    public static IEnumerable<VariableBinding> Collected(PlanNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        foreach (var entry in node.Collect ?? [])
        {
            if (entry.List is { } list)
            {
                yield return new VariableBinding(list.Text, BindingKind.Collect, list.Position, node.Path);
            }
        }
    }

    /// <summary>
    /// Every variable the node itself introduces or binds (RF-6's "own" variables): the introduced ones, its captures, its
    /// <c>set</c> entries and <c>collect</c> lists.
    /// </summary>
    public static IEnumerable<string> Own(PlanNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        return Introduced(node).Concat(Captures(node)).Concat(Collected(node)).Select(b => b.Name)
            .Concat((node.Set ?? []).Where(e => e.NameValid).Select(e => e.Name.Name));
    }

    private IEnumerable<VariableReference> InExec(ExecSpec? exec, string node) =>
        (exec?.Argv ?? []).SelectMany(element => InValue(element, node));

    private IEnumerable<VariableReference> InEnv(IReadOnlyList<NamedValue>? env, string node) =>
        (env ?? []).SelectMany(entry => InValue(entry.Value, node));
}
