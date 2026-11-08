using Blokky.Core.Ast;
using Blokky.Core.Reading;

namespace Blokky.Core.Features;

/// <summary>A feature a plan uses, at its first use in document order.</summary>
/// <param name="Feature">The feature id (CF-12).</param>
/// <param name="Position">The key (or value) that uses it first.</param>
/// <param name="Node">The path id of that node.</param>
public sealed record FeatureUse(string Feature, PlanPosition Position, string Node);

/// <summary>
/// Derives the feature ids a plan uses from its AST (CF-11, architecture.md §3), with the pinned vocabulary of
/// <see cref="FeatureIds"/>. The derivation follows blokky-specs <c>scripts/check.py</c>, which checks every case's
/// <c>features</c> against its plan, so the gate and the suite agree on what a plan uses. Shell ids are derived only from
/// an explicit <c>shell</c> (a script without one uses the configured or detected shell), as there.
/// </summary>
public static class FeatureDeriver
{
    /// <summary>The features of <paramref name="plan"/>, ordered by first use.</summary>
    public static IReadOnlyList<FeatureUse> Derive(PlanAst plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var found = new Dictionary<string, FeatureUse>(StringComparer.Ordinal);
        var root = plan.Root;
        foreach (var key in root.Keys)
        {
            if (key.Name is "inputs" or "vars" or "outputs" or "defaults")
            {
                Add(found, key.Name, key.Position, "/");
            }
        }

        if (plan.Defaults?.Shell is { } shell)
        {
            Add(found, ShellId(shell.Value), shell.Position, "/");
        }

        foreach (var entry in plan.Vars ?? [])
        {
            Value(found, entry.Value, "/", template: true);
        }

        foreach (var node in PlanWalk.Nodes(plan))
        {
            Node(found, node);
        }

        return [.. found.Values.OrderBy(u => u.Position.Line).ThenBy(u => u.Position.Column)];
    }

    private static void Node(Dictionary<string, FeatureUse> found, PlanNode node)
    {
        var p = node.Path;
        foreach (var key in node.Keys)
        {
            var at = key.Position;
            switch (key.Name)
            {
                case "exec" when node.Exec is { IsString: false }:
                    Add(found, "exec", at, p);
                    break;
                case "script" or "do" or "all" or "any" or "race" or "if" or "sleep" or "log" or "fail" or "assert" or "set" or "return"
                    or "stdin" or "encoding" or "when" or "timeout" or "lock" or "tempdir" or "cwd" or "env" or "export" or "container"
                    or "services" or "finally" or "while" or "until" or "backoff" or "jitter" or "collect" or "carry" or "index":
                    Add(found, key.Name, at, p);
                    break;
                case "ok_exit":
                    Add(found, "ok-exit", at, p);
                    break;
                case "stderr":
                    Add(found, "stderr-merge", at, p);
                    break;
                case "capture_file":
                    Add(found, "capture-file", at, p);
                    break;
                case "ignore_errors":
                    Add(found, "ignore-errors", at, p);
                    break;
                case "attempt_timeout":
                    Add(found, "attempt-timeout", at, p);
                    break;
                case "keep_descendants":
                    var detach = node.KeepDescendants?.Value == KeepDescendants.Detach;
                    Add(found, detach ? "keep-descendants-detach" : "keep-descendants", at, p);
                    break;
                case "capture":
                    Add(found, node.Capture?.Fields is null ? "capture" : "capture-fields", at, p);
                    break;
                case "secret" when node.Secret?.Value == true:
                    Add(found, "secret", at, p);
                    break;
                case "retry":
                    Add(found, node.Retry?.Condition is null ? "retry" : "retry-condition", at, p);
                    break;
                case "for" when node.For is { } loop:
                    For(found, loop, p);
                    break;
            }
        }

        Process(found, node.Exec, node.Script, node.Shell, node.Env, p);
        foreach (var (_, condition) in PlanWalk.Conditions(node))
        {
            Condition(found, condition, p);
        }

        foreach (var display in new[] { node.Name, node.Log, node.Fail, node.Message, node.Return })
        {
            if (display is not null && Interpolation.HasPlaceholder(display.Text))
            {
                Add(found, "interpolation", display.Position, p);
            }
        }

        foreach (var entry in node.Set ?? [])
        {
            Value(found, entry.Value, p, template: true);
        }

        Value(found, node.Cwd, p);
        Value(found, node.Stdin, p);
        foreach (var service in node.Services ?? [])
        {
            Process(found, service.Exec, service.Script, service.Shell, service.Env, p);
            if (service.Exec is { IsString: false } exec)
            {
                Add(found, "exec", exec.Position, p);
            }

            if (service.Export is not null && service.Key("export") is { } export)
            {
                Add(found, "export", export.Position, p);
            }
        }
    }

    private static void For(Dictionary<string, FeatureUse> found, ForSpec loop, string node)
    {
        foreach (var key in loop.Keys)
        {
            switch (key.Name)
            {
                case "each" or "count" or "glob" or "items" or "lines" or "matrix":
                    Add(found, "for-" + key.Name, key.Position, node);
                    break;
                case "concurrency":
                    Add(found, "for-concurrency", key.Position, node);
                    break;
            }
        }
    }

    /// <summary>A node's, probe's or service's process: the exec string form, an explicit script shell, env references.</summary>
    private static void Process(Dictionary<string, FeatureUse> found, ExecSpec? exec, PlanScalar? script, Typed<ShellName>? shell,
        IReadOnlyList<NamedValue>? env, string node)
    {
        if (exec is { IsString: true })
        {
            Add(found, "exec-string", exec.Position, node);
        }

        if (script is not null && shell is not null)
        {
            Add(found, ShellId(shell.Value), shell.Position, node);
        }

        foreach (var arg in exec?.Argv ?? [])
        {
            Value(found, arg, node);
        }

        foreach (var entry in env ?? [])
        {
            Value(found, entry.Value, node);
        }
    }

    private static void Condition(Dictionary<string, FeatureUse> found, Condition root, string node)
    {
        foreach (var c in PlanWalk.Within(root))
        {
            foreach (var key in c.Keys)
            {
                switch (key.Name)
                {
                    case "all" or "any" or "not":
                        Add(found, "cond-composite", key.Position, node);
                        break;
                    case "var" or "env" or "path" or "command" or "host" or "result" or "probe":
                        Add(found, "cond-" + key.Name, key.Position, node);
                        break;
                    case "json":
                        Add(found, "cond-json", key.Position, node);
                        break;
                    case "gt" or "ge" or "lt" or "le":
                        Add(found, "cond-numeric", key.Position, node);
                        break;
                }
            }

            foreach (var subject in c.Subjects)
            {
                if (subject.Kind == SubjectKind.Path)
                {
                    Value(found, subject.Value, node);
                }

                if (subject.Probe is { } probe)
                {
                    Process(found, probe.Exec, probe.Script, probe.Shell, probe.Env, node);
                    if (probe.Key("export") is { } export)
                    {
                        Add(found, "export", export.Position, node);
                    }
                }
            }
        }
    }

    /// <summary>A value's references (<c>from-env</c>, <c>secret-ref</c>) or, in a template, its interpolation.</summary>
    private static void Value(Dictionary<string, FeatureUse> found, PlanValue? value, string node, bool template = false)
    {
        switch (value)
        {
            case ReferenceValue reference:
                if (reference.Kind == ReferenceKind.FromEnv)
                {
                    Add(found, "from-env", reference.Position, node);
                }

                if (reference.IsSecret)
                {
                    Add(found, "secret-ref", reference.Position, node);
                }

                break;
            case LiteralValue literal when template && Interpolation.HasPlaceholder(literal.Scalar.Text):
                Add(found, "interpolation", literal.Position, node);
                break;
        }
    }

    private static string ShellId(ShellName shell) => shell switch
    {
        ShellName.Bash => "shell-bash",
        ShellName.Sh => "shell-sh",
        ShellName.Pwsh => "shell-pwsh",
        ShellName.Powershell => "shell-powershell",
        _ => "shell-zsh",
    };

    private static void Add(Dictionary<string, FeatureUse> found, string feature, PlanPosition at, string node)
    {
        if (!found.TryGetValue(feature, out var seen) || SyntaxTree.Compare(at, seen.Position) < 0)
        {
            found[feature] = new FeatureUse(feature, at, node);
        }
    }
}

/// <summary><c>${X}</c> placeholders (SY-82 to SY-84).</summary>
public static class Interpolation
{
    /// <summary>Whether <paramref name="text"/> has a <c>${</c> that isn't the escape <c>$${</c>.</summary>
    public static bool HasPlaceholder(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        for (var i = text.IndexOf("${", StringComparison.Ordinal); i >= 0; i = text.IndexOf("${", i + 1, StringComparison.Ordinal))
        {
            if (i == 0 || text[i - 1] != '$')
            {
                return true;
            }
        }

        return false;
    }
}
