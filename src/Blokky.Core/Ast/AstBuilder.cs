using System.Globalization;
using Blokky.Core.Reading;
using Blokky.Core.Reading.Yaml;
using Blokky.Core.Validation;

namespace Blokky.Core.Ast;

/// <summary>
/// Builds the typed AST from the plan's syntax tree and reports every schema-level error on the way (SY-3): the document
/// root and <c>blokky:</c> (SY-32 to SY-35), plan-only keys below the root (SY-33), unknown keys with suggestions (SY-19,
/// SY-30, SY-48), kind detection (RF-1), required keys (CP-40), field types (SY-88, SY-89, SY-47, CO-4 for a string
/// condition), the scalar grammars (SY-51 to SY-59, SY-96, CO-19), enumerations, and empty values, lists and mappings
/// (SY-20, RF-21, SY-22). A value that is wrong is left out of the AST and the rest is still built. One pass over the
/// tree; recursion depth is bounded by the nesting cap (SY-9).
/// </summary>
/// <remarks>
/// Rules that relate keys, values or scopes are the static rule families' (<see cref="IPlanRule"/>). Where those name a
/// mistake the builder would otherwise report as a type error, the builder records it and stays silent: a reference or
/// secret literal in a field that doesn't take one (<see cref="PlanAst.MisplacedReferences"/>, RF-9, RF-19), and a node
/// used as a condition (<see cref="Condition.NodeKindKey"/>, CO-4).
/// </remarks>
public sealed class AstBuilder
{
    private static readonly (string Text, ShellName Value)[] ShellNames =
    [
        ("bash", ShellName.Bash), ("sh", ShellName.Sh), ("pwsh", ShellName.Pwsh), ("powershell", ShellName.Powershell),
        ("zsh", ShellName.Zsh),
    ];

    private static readonly (string, LogLevel)[] LogLevels = [("debug", LogLevel.Debug), ("info", LogLevel.Info), ("warn", LogLevel.Warn)];

    private static readonly (string, OutputEncoding)[] Encodings =
    [
        ("utf-8", OutputEncoding.Utf8), ("utf-16le", OutputEncoding.Utf16Le), ("utf-16be", OutputEncoding.Utf16Be),
        ("latin1", OutputEncoding.Latin1),
    ];

    private static readonly (string, KeepDescendants)[] KeepValues =
        [("false", KeepDescendants.No), ("true", KeepDescendants.Yes), ("detach", KeepDescendants.Detach)];

    private static readonly (string, TempdirRemove)[] RemoveValues =
        [("false", TempdirRemove.No), ("true", TempdirRemove.Yes), ("on_success", TempdirRemove.OnSuccess)];

    private static readonly (string, BodyFailure)[] BodyFailures = [("stop", BodyFailure.Stop), ("continue", BodyFailure.Continue)];

    private static readonly (string, ServiceExit)[] ServiceExits = [("fail", ServiceExit.Fail), ("ignore", ServiceExit.Ignore)];

    private static readonly (string, string)[] MergeValue = [("merge", "merge")];

    private static readonly (string, string)[] HostValues = [("os", "os"), ("arch", "arch")];

    private static readonly (string, string)[] ResultValues =
        [("stdout", "stdout"), ("stderr", "stderr"), ("exit", "exit"), ("error", "error")];

    private static readonly (string, string)[] OutputValues = [("stdout", "stdout"), ("stderr", "stderr")];

    private readonly PlanErrorSink _errors;
    private readonly List<PlanScalar> _scalars = [];
    private readonly List<MisplacedReference> _misplaced = [];

    private AstBuilder(PlanErrorSink errors) => _errors = errors;

    /// <summary>
    /// Builds the AST of the plan whose root mapping is <paramref name="root"/>, reporting into <paramref name="errors"/>.
    /// </summary>
    public static PlanAst Build(SyntaxMapping root, PlanErrorSink errors)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(errors);
        var builder = new AstBuilder(errors);
        var node = new PlanNode(root.Start, "/");
        var plan = new PlanAst(node);
        if (root.Entries.Count == 0)
        {
            errors.Value("SY-22", root.Start, "/", "the plan is an empty mapping: it needs blokky: and a root node", "blokky: 1.0");
        }
        else
        {
            builder.Node(node, root, plan);
            if (!node.Has("blokky"))
            {
                errors.Version("SY-34", root.Start, "/", "the plan has no blokky: version; add blokky: 1.0", Grammars.SupportedVersions,
                    "blokky: 1.0");
            }
        }

        plan.Scalars = builder._scalars;
        plan.MisplacedReferences = builder._misplaced;
        return plan;
    }

    /// <summary>The path id of a node in a body field (reference.md §1).</summary>
    public static string ChildPath(string parent, string field, int index) =>
        string.Create(CultureInfo.InvariantCulture, $"{(parent == "/" ? "" : parent)}/{field}/{index}");

    // ---- nodes ----------------------------------------------------------------------------------------------------

    private void Node(PlanNode node, SyntaxMapping m, PlanAst? plan)
    {
        var kinds = new List<PlanKey>();
        var shape = plan is null ? MappingShape.Node : MappingShape.Root;
        foreach (var (k, v) in m.Entries)
        {
            var key = new PlanKey(k.Value, k.Start);
            var planOnly = KeyShapes.PlanOnly.Contains(k.Value);
            if (planOnly && plan is null)
            {
                _errors.Invalid("SY-33", k.Start, node.Path, $"{k.Value}: is allowed only at the root of the plan");
                continue;
            }

            if (!KeyShapes.IsKey(shape, k.Value))
            {
                Unknown(shape, k, node.Path);
                continue;
            }

            node.AddKey(key);
            if (KeyShapes.Kinds.Contains(k.Value))
            {
                kinds.Add(key);
            }

            if (v is SyntaxAlias)
            {
                continue;
            }

            if (planOnly)
            {
                PlanField(plan!, key, v);
                continue;
            }

            if (IsEmpty(v))
            {
                EmptyValue(key, node.Path);
                continue;
            }

            NodeField(node, key, v);
        }

        if (kinds.Count == 0)
        {
            _errors.Invalid("RF-1", m.Start, node.Path,
                "this node has no kind: add exactly one of exec, script, do, all, any, race, if, sleep, log, fail, assert, set, return");
            return;
        }

        node.KindKey = kinds[0];
        node.Kind = (NodeKind)IndexOf(KeyShapes.Kinds, kinds[0].Name);
        foreach (var extra in kinds.Skip(1))
        {
            if (extra.Name == "if" || kinds[0].Name == "if")
            {
                _errors.Invalid("RF-1", extra.Position, node.Path,
                    $"'{kinds[0].Name}' and '{extra.Name}' are two kinds in one node: if is a kind with then/else, not a guard; "
                    + "use when: for a guard", "when: { var: X, equals: '1' }");
            }
            else
            {
                _errors.Invalid("RF-1", extra.Position, node.Path,
                    $"'{kinds[0].Name}' and '{extra.Name}' are two kinds in one node: split them into two nodes in a do: list");
            }
        }
    }

    private void PlanField(PlanAst plan, PlanKey key, SyntaxNode v)
    {
        if (IsEmpty(v))
        {
            if (key.Name == "blokky")
            {
                _errors.Version("SY-34", key.Position, "/", "blokky: has no value; write blokky: 1.0", Grammars.SupportedVersions,
                    "blokky: 1.0");
            }
            else
            {
                EmptyValue(key, "/");
            }

            return;
        }

        switch (key.Name)
        {
            case "blokky":
                Version(plan, key, v);
                break;
            case "inputs":
                plan.Inputs = Named(v, key, "inputs", "/", Grammars.VariableName, (name, valid, value) => Input(name, valid, value));
                break;
            case "vars":
                plan.Vars = Named(v, key, "vars", "/", Grammars.VariableName,
                    (name, valid, value) => new NamedValue(name, valid, value is null ? null
                        : Value(value, "vars", "/", FieldForm.Template, allowVar: true, allowFromEnv: true)));
                break;
            case "outputs":
                plan.Outputs = Names(v, "outputs", "/", Grammars.VariableName);
                break;
            case "defaults":
                plan.Defaults = Defaults(v, key);
                break;
        }
    }

    private void Version(PlanAst plan, PlanKey key, SyntaxNode v)
    {
        var s = Scalar(v, "blokky", "/", FieldForm.Name, "a version such as 1.0", version: true);
        if (s is null)
        {
            return;
        }

        plan.Version = s;
        if (!Grammars.IsSpecVersion(s.Text))
        {
            _errors.Version("SY-51", s.Position, "/", $"'{s.Text}' isn't a spec version: write MAJOR.MINOR, such as 1.0 (no patch version)",
                Grammars.SupportedVersions, "blokky: 1.0");
        }
        else if (!Grammars.SupportedVersions.Contains(s.Text))
        {
            _errors.Version("SY-35", s.Position, "/",
                $"this build of blokky doesn't support spec version {s.Text}; supported: {string.Join(", ", Grammars.SupportedVersions)}",
                Grammars.SupportedVersions);
        }
        else
        {
            plan.SpecVersion = s.Text;
        }

        _ = key;
    }

    private InputDecl? Input(PlanKey name, bool valid, SyntaxNode? v)
    {
        if (v is null)
        {
            return null;
        }

        var m = Mapping(v, "inputs", "/", "an input is a mapping, such as { description: … } or { default: … }");
        if (m is null)
        {
            return null;
        }

        var input = new InputDecl(name, valid, m.Start);
        Fields(m, MappingShape.Input, "/", input, (key, value) =>
        {
            switch (key.Name)
            {
                case "default":
                    if (value is SyntaxMapping)
                    {
                        _errors.Value("SY-47", value.Start, "/",
                            "an input's default is a literal: a reference or variable can't be a default (defaults are literal)");
                    }
                    else
                    {
                        input.Default = Scalar(value, "inputs.default", "/", FieldForm.Literal, "a string");
                    }

                    break;
                case "description":
                    input.Description = Scalar(value, "inputs.description", "/", FieldForm.Literal, "a string");
                    break;
                case "in":
                    input.In = Strings(value, "inputs.in", "/");
                    break;
                case "matches":
                    input.Matches = Scalar(value, "inputs.matches", "/", FieldForm.Literal, "a regex");
                    break;
            }
        });
        return input;
    }

    private DefaultsSpec? Defaults(SyntaxNode v, PlanKey key)
    {
        var m = Mapping(v, key.Name, "/", "a mapping with shell and leaf_timeout");
        if (m is null)
        {
            return null;
        }

        var defaults = new DefaultsSpec(m.Start);
        Fields(m, MappingShape.Defaults, "/", defaults, (k, value) =>
        {
            if (k.Name == "shell")
            {
                defaults.Shell = Enum(value, "defaults.shell", "/", ShellNames);
            }
            else
            {
                defaults.LeafTimeout = Duration(value, "defaults.leaf_timeout", "/", zeroAllowed: false);
            }
        });
        return defaults;
    }

    private void NodeField(PlanNode node, PlanKey key, SyntaxNode v)
    {
        var p = node.Path;
        var f = key.Name;
        switch (f)
        {
            case "exec":
                node.Exec = Exec(v, f, p);
                break;
            case "script":
                node.Script = Script(v, f, p);
                break;
            case "do":
                node.Do = Body(v, f, p);
                break;
            case "all":
                node.All = Body(v, f, p);
                break;
            case "any":
                node.Any = Body(v, f, p);
                break;
            case "race":
                node.Race = Body(v, f, p);
                break;
            case "then":
                node.Then = Body(v, f, p);
                break;
            case "else":
                node.Else = Body(v, f, p);
                break;
            case "finally":
                node.Finally = Body(v, f, p);
                break;
            case "if":
                node.If = Condition(v, f, p);
                break;
            case "assert":
                node.Assert = Condition(v, f, p);
                break;
            case "when":
                node.When = Condition(v, f, p);
                break;
            case "while":
                node.While = Condition(v, f, p);
                break;
            case "until":
                node.Until = Condition(v, f, p);
                break;
            case "sleep":
                node.Sleep = Duration(v, f, p, zeroAllowed: true);
                break;
            case "log":
                node.Log = Scalar(v, f, p, FieldForm.Display, "a message");
                break;
            case "fail":
                node.Fail = Scalar(v, f, p, FieldForm.Display, "a message");
                break;
            case "return":
                node.Return = Scalar(v, f, p, FieldForm.Display, "a message");
                break;
            case "name":
                node.Name = Scalar(v, f, p, FieldForm.Display, "a string");
                break;
            case "message":
                node.Message = Scalar(v, f, p, FieldForm.Display, "a message");
                break;
            case "set":
                node.Set = Named(v, key, f, p, Grammars.VariableName,
                    (name, valid, value) => new NamedValue(name, valid, value is null ? null
                        : Value(value, f, p, FieldForm.Template, allowVar: true, allowFromEnv: true)));
                break;
            case "shell":
                node.Shell = Enum(v, f, p, ShellNames);
                break;
            case "level":
                node.Level = Enum(v, f, p, LogLevels);
                break;
            case "code":
                node.Code = Name(v, f, p, Grammars.UserCode);
                break;
            case "concurrency":
                node.Concurrency = Integer(v, f, p, 1, int.MaxValue);
                break;
            case "fail_fast":
                node.FailFast = Boolean(v, f, p);
                break;
            case "stdin":
                node.Stdin = Value(v, f, p, FieldForm.Reference, allowVar: true, allowFile: true);
                break;
            case "ok_exit":
                node.OkExit = ExitCodes(v, f, p);
                break;
            case "stderr":
                node.Stderr = Enum(v, f, p, MergeValue)?.Source;
                break;
            case "encoding":
                node.Encoding = Enum(v, f, p, Encodings);
                break;
            case "keep_descendants":
                node.KeepDescendants = Enum(v, f, p, KeepValues, booleanLike: true);
                break;
            case "capture":
                node.Capture = Capture(v, f, p);
                break;
            case "capture_file":
                node.CaptureFile = Name(v, f, p, Grammars.VariableName);
                break;
            case "secret":
                node.Secret = Boolean(v, f, p);
                break;
            case "timeout":
                node.Timeout = Duration(v, f, p, zeroAllowed: false);
                break;
            case "finally_timeout":
                node.FinallyTimeout = Duration(v, f, p, zeroAllowed: false);
                break;
            case "attempt_timeout":
                node.AttemptTimeout = Duration(v, f, p, zeroAllowed: false);
                break;
            case "every":
                node.Every = Duration(v, f, p, zeroAllowed: true);
                break;
            case "lock":
                node.Lock = Lock(v, f, p);
                break;
            case "tempdir":
                node.Tempdir = Tempdir(v, f, p);
                break;
            case "cwd":
                node.Cwd = Value(v, f, p, FieldForm.Reference, allowVar: true, nonEmpty: true);
                break;
            case "env":
                node.Env = Env(v, key, f, p);
                break;
            case "export":
                node.Export = Names(v, f, p, Grammars.VariableName);
                break;
            case "container":
                node.Container = Container(v, f, p);
                break;
            case "services":
                node.Services = Items(v, f, p, allowEmpty: false, item => Service(item, p));
                break;
            case "ignore_errors":
                node.IgnoreErrors = Boolean(v, f, p);
                break;
            case "retry":
                node.Retry = Retry(v, f, p);
                break;
            case "for":
                node.For = For(v, f, p);
                break;
            case "max":
                node.Max = Integer(v, f, p, 1, int.MaxValue);
                break;
            case "backoff":
                node.Backoff = Backoff(v, f, p);
                break;
            case "jitter":
                node.Jitter = Decimal(v, f, p, 0m, 1m, minExclusive: true);
                break;
            case "on_body_failure":
                node.OnBodyFailure = Enum(v, f, p, BodyFailures);
                break;
            case "index":
                node.Index = Index(v, f, p);
                break;
            case "carry":
                node.Carry = Named(v, key, f, p, Grammars.VariableName, (name, valid, value) => Carry(name, valid, value, p));
                break;
            case "collect":
                node.Collect = Named(v, key, f, p, Grammars.VariableName,
                    (name, valid, value) => new CollectEntry(name, valid, value is null ? null : Name(value, f, p, Grammars.VariableName)));
                break;
        }
    }

    /// <summary>A body field (<c>do</c>, <c>then</c>, <c>finally</c> …): a non-empty list of nodes (SY-88, RF-21).</summary>
    private List<PlanNode>? Body(SyntaxNode v, string field, string parent)
    {
        if (v is SyntaxMapping m && LooksLikeCondition(m))
        {
            _errors.Value("CO-4", v.Start, parent, $"{field}: takes a list of nodes, not a condition: put the condition in if: or when:");
            return null;
        }

        if (v is not SyntaxSequence)
        {
            _errors.Value("SY-88", v.Start, parent, v is SyntaxMapping
                ? $"{field}: takes a list of nodes, not a single node: prefix each node with a dash (- exec: …)"
                : $"{field}: takes a list of nodes",
                $"{field}:\n  - exec: …");
            return null;
        }

        var index = 0;
        return Items(v, field, parent, allowEmpty: false, item =>
        {
            var path = ChildPath(parent, field, index++);
            if (item is not SyntaxMapping child)
            {
                _errors.Value("SY-88", item.Start, parent, $"an entry of {field}: is a node, a mapping such as exec: …");
                return null;
            }

            if (child.Entries.Count == 0)
            {
                _errors.Value("SY-22", child.Start, parent, "an empty mapping isn't a node");
                return null;
            }

            var node = new PlanNode(child.Start, path);
            Node(node, child, null);
            return node;
        });
    }

    private static bool LooksLikeCondition(SyntaxMapping m) =>
        m.Entries.Count > 0
        && m.Entries.All(e => !KeyShapes.IsKey(MappingShape.Node, e.Key.Value))
        && m.Entries.Any(e => KeyShapes.IsKey(MappingShape.Condition, e.Key.Value));

    // ---- process specs --------------------------------------------------------------------------------------------

    private ExecSpec? Exec(SyntaxNode v, string field, string node)
    {
        switch (v)
        {
            case SyntaxScalar:
                return new ExecSpec(v.Start, Scalar(v, field, node, FieldForm.Literal, "a command"), null);
            case SyntaxSequence:
                var index = 0;
                var argv = Items(v, field, node, allowEmpty: false, item =>
                    index++ == 0
                        ? Value(item, field, node, FieldForm.Literal, allowVar: false, nonEmpty: true)
                        : Value(item, field, node, FieldForm.Reference, allowVar: true));
                return argv is null ? null : new ExecSpec(v.Start, null, argv);
            case SyntaxAlias:
                return null;
            default:
                if (IsReference((SyntaxMapping)v))
                {
                    Misplace(Value(v, field, node, FieldForm.Literal, record: false), field, node, FieldForm.Literal);
                    return null;
                }

                _errors.Value("SY-88", v.Start, node,
                    $"{field}: takes a command string or an argv list, not a mapping: move timeout and other options to the node",
                    $"{field}: [prog, arg]\ntimeout: 5m");
                return null;
        }
    }

    private PlanScalar? Script(SyntaxNode v, string field, string node)
    {
        if (v is SyntaxSequence { Flow: true })
        {
            _errors.Value("SY-89", v.Start, node,
                $"{field}: a value starting with [ is a YAML flow sequence, not text: quote it or use a | block scalar",
                $"{field}: '[ -f x ]'");
            return null;
        }

        if (v is SyntaxMapping m && !IsReference(m) && !HasSecret(m))
        {
            _errors.Value("SY-88", v.Start, node,
                $"{field}: takes shell text, not a mapping: move timeout and other options to the node", $"{field}: |\n  …");
            return null;
        }

        return Scalar(v, field, node, FieldForm.ShellText, "shell text", nonEmpty: true);
    }

    private Probe? Probe(SyntaxNode v, string node)
    {
        var m = Mapping(v, "probe", node, "a probe is a mapping with exec or script");
        if (m is null)
        {
            return null;
        }

        var probe = new Probe(m.Start, node);
        Fields(m, MappingShape.Probe, node, probe, (k, value) =>
        {
            var f = "probe." + k.Name;
            switch (k.Name)
            {
                case "exec":
                    probe.Exec = Exec(value, f, node);
                    break;
                case "script":
                    probe.Script = Script(value, f, node);
                    break;
                case "shell":
                    probe.Shell = Enum(value, f, node, ShellNames);
                    break;
                case "env":
                    probe.Env = Env(value, k, f, node);
                    break;
                case "export":
                    probe.Export = Names(value, f, node, Grammars.VariableName);
                    break;
                case "timeout":
                    probe.Timeout = Duration(value, f, node, zeroAllowed: false);
                    break;
                case "true_exit":
                    probe.TrueExit = ExitCodes(value, f, node);
                    break;
                case "false_exit":
                    probe.FalseExit = ExitCodes(value, f, node);
                    break;
                case "timeout_result":
                    probe.TimeoutResult = Boolean(value, f, node);
                    break;
            }
        });
        return probe;
    }

    private ServiceSpec? Service(SyntaxNode v, string node)
    {
        var m = Mapping(v, "services", node, "a service is a mapping with name, exec or script, and ready");
        if (m is null)
        {
            return null;
        }

        var service = new ServiceSpec(m.Start, node);
        Fields(m, MappingShape.Service, node, service, (k, value) =>
        {
            var f = "services." + k.Name;
            switch (k.Name)
            {
                case "name":
                    service.Name = Name(value, f, node, s => Grammars.LowerName(s, "service name"));
                    break;
                case "exec":
                    service.Exec = Exec(value, f, node);
                    break;
                case "script":
                    service.Script = Script(value, f, node);
                    break;
                case "shell":
                    service.Shell = Enum(value, f, node, ShellNames);
                    break;
                case "env":
                    service.Env = Env(value, k, f, node);
                    break;
                case "export":
                    service.Export = Names(value, f, node, Grammars.VariableName);
                    break;
                case "cwd":
                    service.Cwd = Value(value, f, node, FieldForm.Reference, allowVar: true, nonEmpty: true);
                    break;
                case "ready":
                    service.Ready = Condition(value, f, node);
                    break;
                case "ready_timeout":
                    service.ReadyTimeout = Duration(value, f, node, zeroAllowed: false);
                    break;
                case "on_exit":
                    service.OnExit = Enum(value, f, node, ServiceExits);
                    break;
            }
        });
        return service;
    }

    private List<NamedValue>? Env(SyntaxNode v, PlanKey key, string field, string node) =>
        Named(v, key, field, node, Grammars.EnvironmentName,
            (name, valid, value) => new NamedValue(name, valid, value is null ? null
                : Value(value, field, node, FieldForm.Reference, allowVar: true, allowFromEnv: true)));

    private CaptureSpec? Capture(SyntaxNode v, string field, string node)
    {
        if (v is SyntaxMapping m && !IsReference(m) && !HasSecret(m))
        {
            var fields = Named(v, new PlanKey(field, v.Start), field, node, Grammars.VariableName,
                (name, valid, value) => new CaptureField(name, valid,
                    value is null ? null : Scalar(value, field, node, FieldForm.Literal, "a JSON path")));
            return fields is null ? null : new CaptureSpec(v.Start, null, fields);
        }

        var single = Name(v, field, node, Grammars.VariableName);
        return single is null ? null : new CaptureSpec(v.Start, single, null);
    }

    // ---- modifiers ------------------------------------------------------------------------------------------------

    private LockSpec? Lock(SyntaxNode v, string field, string node)
    {
        static GrammarError? LockName(string s) => Grammars.LowerName(s, "lock name");
        if (v is SyntaxScalar)
        {
            var name = Name(v, field, node, LockName);
            return name is null ? null : new LockSpec(v.Start, node) { Name = name };
        }

        var m = Mapping(v, field, node, "a lock name or { name, wait }");
        if (m is null)
        {
            return null;
        }

        var spec = new LockSpec(m.Start, node);
        Fields(m, MappingShape.Lock, node, spec, (k, value) =>
        {
            if (k.Name == "name")
            {
                spec.Name = Name(value, "lock.name", node, LockName);
            }
            else
            {
                spec.Wait = Duration(value, "lock.wait", node, zeroAllowed: true);
            }
        });
        Require(spec, m, "name", "lock", node);
        return spec;
    }

    private TempdirSpec? Tempdir(SyntaxNode v, string field, string node)
    {
        if (v is SyntaxScalar)
        {
            return TrueOnly(v, field, node) ? new TempdirSpec(v.Start, node) : null;
        }

        var m = Mapping(v, field, node, "true or { as, prefix, remove }");
        if (m is null)
        {
            return null;
        }

        var spec = new TempdirSpec(m.Start, node);
        Fields(m, MappingShape.Tempdir, node, spec, (k, value) =>
        {
            switch (k.Name)
            {
                case "as":
                    spec.As = Name(value, "tempdir.as", node, Grammars.VariableName);
                    break;
                case "prefix":
                    spec.Prefix = Name(value, "tempdir.prefix", node, Grammars.TempdirPrefix);
                    break;
                case "remove":
                    spec.Remove = Enum(value, "tempdir.remove", node, RemoveValues, booleanLike: true);
                    break;
            }
        });
        return spec;
    }

    private ContainerSpec? Container(SyntaxNode v, string field, string node)
    {
        var m = Mapping(v, field, node, "a mapping with image, mounts and env_pass");
        if (m is null)
        {
            return null;
        }

        var spec = new ContainerSpec(m.Start, node);
        Fields(m, MappingShape.Container, node, spec, (k, value) =>
        {
            switch (k.Name)
            {
                case "image":
                    spec.Image = Scalar(value, "container.image", node, FieldForm.Literal, "an image reference", nonEmpty: true);
                    break;
                case "mounts":
                    spec.Mounts = Items(value, "container.mounts", node, allowEmpty: true, item => Mount(item, node));
                    break;
                case "env_pass":
                    spec.EnvPass = Names(value, "container.env_pass", node, Grammars.EnvironmentName);
                    break;
            }
        });
        if (!spec.Has("image"))
        {
            _errors.Invalid("CP-40", m.Start, node, "container: needs an image");
        }

        return spec;
    }

    private MountSpec? Mount(SyntaxNode v, string node)
    {
        var m = Mapping(v, "container.mounts", node, "a mount is a mapping with host and path");
        if (m is null)
        {
            return null;
        }

        var mount = new MountSpec(m.Start, node);
        Fields(m, MappingShape.Mount, node, mount, (k, value) =>
        {
            switch (k.Name)
            {
                case "host":
                    mount.Host = Value(value, "container.mounts.host", node, FieldForm.Reference, allowVar: true, nonEmpty: true);
                    break;
                case "path":
                    mount.Path = Scalar(value, "container.mounts.path", node, FieldForm.Literal, "a container path", nonEmpty: true);
                    break;
                case "read_only":
                    mount.ReadOnly = Boolean(value, "container.mounts.read_only", node);
                    break;
            }
        });
        foreach (var required in new[] { "host", "path" })
        {
            if (!mount.Has(required))
            {
                _errors.Invalid("CP-40", m.Start, node, $"a mount needs {required}");
            }
        }

        return mount;
    }

    // ---- repetition -----------------------------------------------------------------------------------------------

    private RetrySpec? Retry(SyntaxNode v, string field, string node)
    {
        if (v is not SyntaxScalar s)
        {
            var condition = Condition(v, field, node);
            return condition is null ? null : new RetrySpec(v.Start, condition);
        }

        if (s.Value == "true")
        {
            Scalar(v, field, node, FieldForm.Name, "true");
            return new RetrySpec(v.Start, null);
        }

        if (s.Value.Length > 0 && !s.Value.AsSpan().ContainsAnyExceptInRange('0', '9'))
        {
            var attempts = long.TryParse(s.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var n) && n < int.MaxValue
                ? (n + 1).ToString(CultureInfo.InvariantCulture)
                : "N+1";
            _errors.Value("SY-88", v.Start, node,
                $"retry: takes true or a condition, not a number: write retry: true plus max: {attempts} "
                    + "(max counts attempts, including the first)",
                $"retry: true\nmax: {attempts}");
        }
        else if (Grammars.Boolean(s.Value, out _) is { Fix: not null } || s.Value == "false")
        {
            _errors.Value("SY-53", v.Start, node, $"retry: '{s.Value}': the only scalar here is true", "retry: true");
        }
        else
        {
            StringCondition(v, field, node);
        }

        return null;
    }

    private ForSpec? For(SyntaxNode v, string field, string node)
    {
        var m = Mapping(v, field, node, "a mapping with a source (each, count, glob, items, lines, matrix) and as");
        if (m is null)
        {
            return null;
        }

        var spec = new ForSpec(m.Start, node);
        Fields(m, MappingShape.For, node, spec, (k, value) =>
        {
            var f = "for." + k.Name;
            switch (k.Name)
            {
                case "each":
                    spec.Each = Strings(value, f, node);
                    break;
                case "count":
                    spec.Count = Integer(value, f, node, 0, int.MaxValue);
                    break;
                case "glob":
                    spec.Glob = Scalar(value, f, node, FieldForm.Literal, "a glob pattern", nonEmpty: true);
                    break;
                case "items":
                    var items = Mapping(value, f, node, "{ var: X, json: .path }");
                    if (items is not null)
                    {
                        var holder = new ForSpec(items.Start, node);
                        Fields(items, MappingShape.ForItems, node, holder, (ik, iv) =>
                        {
                            if (ik.Name == "var")
                            {
                                spec.ItemsVar = Name(iv, "for.items.var", node, Grammars.VariableName);
                            }
                            else
                            {
                                spec.ItemsJson = Scalar(iv, "for.items.json", node, FieldForm.Literal, "a JSON path");
                            }
                        });
                        Require(holder, items, "var", "for.items", node);
                    }

                    break;
                case "lines":
                    var lines = Mapping(value, f, node, "{ var: X }");
                    if (lines is not null)
                    {
                        var holder = new ForSpec(lines.Start, node);
                        Fields(lines, MappingShape.ForLines, node, holder,
                            (_, lv) => spec.LinesVar = Name(lv, "for.lines.var", node, Grammars.VariableName));
                        Require(holder, lines, "var", "for.lines", node);
                    }

                    break;
                case "matrix":
                    spec.Matrix = Named(value, k, f, node, Grammars.VariableName,
                        (name, _, axis) => (name, axis is null ? null : (IReadOnlyList<PlanScalar>?)Strings(axis, f, node)));
                    break;
                case "as":
                    spec.As = Name(value, f, node, Grammars.VariableName);
                    break;
                case "concurrency":
                    spec.Concurrency = Integer(value, f, node, 1, int.MaxValue);
                    break;
                case "fail_fast":
                    spec.FailFast = Boolean(value, f, node);
                    break;
                case "allow_empty":
                    spec.AllowEmpty = Boolean(value, f, node);
                    break;
                case "max_items":
                    spec.MaxItems = Integer(value, f, node, 1, int.MaxValue);
                    break;
                case "fields":
                    spec.Fields = Named(value, k, f, node, Grammars.VariableName,
                        (name, valid, path) => new CaptureField(name, valid, path is null ? null
                            : Scalar(path, f, node, FieldForm.Literal, "a JSON path")));
                    break;
                case "keep_blank":
                    spec.KeepBlank = Boolean(value, f, node);
                    break;
            }
        });
        return spec;
    }

    private BackoffSpec? Backoff(SyntaxNode v, string field, string node)
    {
        var m = Mapping(v, field, node, "{ factor: F, max: D }");
        if (m is null)
        {
            return null;
        }

        var spec = new BackoffSpec(m.Start, node);
        Fields(m, MappingShape.Backoff, node, spec, (k, value) =>
        {
            if (k.Name == "factor")
            {
                spec.Factor = Decimal(value, "backoff.factor", node, 1m, 10m, minExclusive: false);
            }
            else
            {
                spec.Max = Duration(value, "backoff.max", node, zeroAllowed: false);
            }
        });
        Require(spec, m, "factor", "backoff", node);
        return spec;
    }

    private IndexSpec? Index(SyntaxNode v, string field, string node)
    {
        if (v is SyntaxScalar)
        {
            var name = Name(v, field, node, Grammars.VariableName);
            return name is null ? null : new IndexSpec(v.Start, node) { As = name };
        }

        var m = Mapping(v, field, node, "a variable name or { as, start }");
        if (m is null)
        {
            return null;
        }

        var spec = new IndexSpec(m.Start, node);
        Fields(m, MappingShape.Index, node, spec, (k, value) =>
        {
            if (k.Name == "as")
            {
                spec.As = Name(value, "index.as", node, Grammars.VariableName);
            }
            else
            {
                spec.Start = Integer(value, "index.start", node, 0, int.MaxValue);
            }
        });
        Require(spec, m, "as", "index", node);
        return spec;
    }

    private CarryEntry Carry(PlanKey name, bool valid, SyntaxNode? v, string node)
    {
        if (v is null or SyntaxScalar)
        {
            return new CarryEntry(name, valid, v is null ? null : Name(v, "carry", node, Grammars.VariableName), null);
        }

        var m = Mapping(v, "carry", node, "a capture name or { from, initial }");
        if (m is null)
        {
            return new CarryEntry(name, valid, null, null);
        }

        PlanScalar? from = null, initial = null;
        var holder = new IndexSpec(m.Start, node);
        Fields(m, MappingShape.Carry, node, holder, (k, value) =>
        {
            if (k.Name == "from")
            {
                from = Name(value, "carry.from", node, Grammars.VariableName);
            }
            else
            {
                initial = Scalar(value, "carry.initial", node, FieldForm.Literal, "a string");
            }
        });
        Require(holder, m, "from", "a carry entry", node);
        return new CarryEntry(name, valid, from, initial);
    }

    // ---- conditions -----------------------------------------------------------------------------------------------

    private Condition? Condition(SyntaxNode v, string field, string node)
    {
        switch (v)
        {
            case SyntaxAlias:
                return null;
            case SyntaxScalar:
                StringCondition(v, field, node);
                return null;
            case SyntaxSequence:
                _errors.Value("SY-88", v.Start, node, $"{field}: takes a condition, a mapping such as {{ var: X, equals: a }}, not a list");
                return null;
        }

        var m = (SyntaxMapping)v;
        if (m.Entries.Count == 0)
        {
            _errors.Value("SY-22", m.Start, node, $"{field}: an empty mapping isn't a condition");
            return null;
        }

        var condition = new Condition(m.Start, node);
        var kind = m.Entries.FirstOrDefault(e => KeyShapes.Kinds.Contains(e.Key.Value) && e.Key.Value is not ("all" or "any"));
        if (kind is not null)
        {
            condition.NodeKindKey = new PlanKey(kind.Key.Value, kind.Key.Start);
            return condition;
        }

        Fields(m, MappingShape.Condition, node, condition, (k, value) =>
        {
            var f = field + "." + k.Name;
            var subject = IndexOf(KeyShapes.Subjects, k.Name);
            if (subject >= 0)
            {
                condition.Add(Subject((SubjectKind)subject, k, value, f, node));
                return;
            }

            var op = IndexOf(KeyShapes.Operators, k.Name);
            if (op >= 0)
            {
                condition.Add(Operator((OperatorKind)op, k, value, f, node));
                return;
            }

            switch (k.Name)
            {
                case "json":
                    condition.Json = Scalar(value, f, node, FieldForm.Literal, "a JSON path");
                    break;
                case "ignore_case":
                    condition.IgnoreCase = Boolean(value, f, node);
                    break;
                case "all":
                    condition.All = Items(value, f, node, allowEmpty: false, item => Condition(item, f, node));
                    break;
                case "any":
                    condition.Any = Items(value, f, node, allowEmpty: false, item => Condition(item, f, node));
                    break;
                case "not":
                    condition.Not = Condition(value, f, node);
                    break;
            }
        });
        return condition;
    }

    private ConditionSubject Subject(SubjectKind kind, PlanKey key, SyntaxNode v, string field, string node)
    {
        PlanValue? value = null;
        Probe? probe = null;
        switch (kind)
        {
            case SubjectKind.Var:
                value = Literal(Name(v, field, node, Grammars.VariableName));
                break;
            case SubjectKind.Env:
                value = Literal(Name(v, field, node, Grammars.EnvironmentName));
                break;
            case SubjectKind.Path:
                value = Value(v, field, node, FieldForm.Reference, allowVar: true, nonEmpty: true);
                break;
            case SubjectKind.Command:
                value = Literal(Scalar(v, field, node, FieldForm.Literal, "a program name", nonEmpty: true));
                break;
            case SubjectKind.Host:
                value = Literal(Enum(v, field, node, HostValues)?.Source);
                break;
            case SubjectKind.Result:
                value = Literal(Enum(v, field, node, ResultValues)?.Source);
                break;
            case SubjectKind.Probe:
                probe = Probe(v, node);
                break;
            case SubjectKind.Port:
                value = Literal(Integer(v, field, node, 1, 65535)?.Source);
                break;
            case SubjectKind.Output:
                value = Literal(Enum(v, field, node, OutputValues)?.Source);
                break;
        }

        return new ConditionSubject(kind, key, value, probe);
    }

    private ConditionOperator Operator(OperatorKind kind, PlanKey key, SyntaxNode v, string field, string node)
    {
        switch (kind)
        {
            case OperatorKind.In or OperatorKind.NotIn:
                return new ConditionOperator(kind, key, null, Strings(v, field, node));
            case OperatorKind.Empty or OperatorKind.NotEmpty or OperatorKind.Defined or OperatorKind.NotDefined or OperatorKind.Exists
                or OperatorKind.NotExists or OperatorKind.IsFile or OperatorKind.IsDir or OperatorKind.Open:
                var unary = TrueOnly(v, field, node) ? Literal(Scalar(v, field, node, FieldForm.Name, "true")) : null;
                return new ConditionOperator(kind, key, unary, null);
            case OperatorKind.Gt or OperatorKind.Ge or OperatorKind.Lt or OperatorKind.Le:
                var number = Scalar(v, field, node, FieldForm.Literal, "a number");
                if (number is not null && Grammars.NumericOperand(number.Text) is { } error)
                {
                    _errors.Value(error.Rule, number.Position, node, error.Message);
                    number = null;
                }

                return new ConditionOperator(kind, key, Literal(number), null);
            default:
                return new ConditionOperator(kind, key, Literal(Scalar(v, field, node, FieldForm.Literal, "a string")), null);
        }
    }

    private void StringCondition(SyntaxNode v, string field, string node) =>
        _errors.Value("CO-4", v.Start, node,
            $"{field}: takes a condition, a mapping with a subject and an operator, not a string",
            $"{field}: {{ var: X, equals: '1' }}");

    // ---- values ---------------------------------------------------------------------------------------------------

    /// <summary>
    /// A scalar field. A reference or secret literal is recorded as misplaced (RF-9, RF-19); other collections are SY-88.
    /// </summary>
    private PlanScalar? Scalar(SyntaxNode v, string field, string node, FieldForm form, string what, bool nonEmpty = false,
        bool version = false)
    {
        switch (v)
        {
            case SyntaxScalar s:
                if (nonEmpty && s.Value.Length == 0)
                {
                    _errors.Value("SY-20", v.Start, node, $"{field}: an empty string isn't allowed here");
                    return null;
                }

                var scalar = new PlanScalar(s, field, form, node);
                _scalars.Add(scalar);
                return scalar;
            case SyntaxMapping m when IsReference(m) || HasSecret(m):
                Misplace(Value(v, field, node, FieldForm.Literal, record: false), field, node, form);
                return null;
            case SyntaxAlias:
                return null;
            default:
                if (version)
                {
                    _errors.Version("SY-51", v.Start, node, $"blokky: takes {what}", Grammars.SupportedVersions, "blokky: 1.0");
                }
                else
                {
                    _errors.Value("SY-88", v.Start, node, $"{field}: takes {what}, not a {(v is SyntaxMapping ? "mapping" : "list")}");
                }

                return null;
        }
    }

    private static LiteralValue? Literal(PlanScalar? scalar) => scalar is null ? null : new LiteralValue(scalar);

    /// <summary>A name field: a scalar matching <paramref name="grammar"/> (SY-52).</summary>
    private PlanScalar? Name(SyntaxNode v, string field, string node, Func<string, GrammarError?> grammar)
    {
        var s = Scalar(v, field, node, FieldForm.Name, "a name");
        if (s is not null && grammar(s.Text) is { } error)
        {
            _errors.Value(error.Rule, s.Position, node, $"{field}: {error.Message}");
            return null;
        }

        return s;
    }

    private Typed<bool>? Boolean(SyntaxNode v, string field, string node)
    {
        var s = Scalar(v, field, node, FieldForm.Name, "true or false");
        if (s is null)
        {
            return null;
        }

        if (Grammars.Boolean(s.Text, out var value) is { } error)
        {
            _errors.Value(error.Rule, s.Position, node, $"{field}: {error.Message}", error.Fix is null ? null : $"{field}: {error.Fix}");
            return null;
        }

        return new Typed<bool>(s, value);
    }

    private bool TrueOnly(SyntaxNode v, string field, string node)
    {
        if (v is SyntaxScalar s && Grammars.TrueOnly(s.Value) is { } error)
        {
            _errors.Value(error.Rule, v.Start, node, $"{field}: {error.Message}", $"{field}: true");
            return false;
        }

        return v is SyntaxScalar || Scalar(v, field, node, FieldForm.Name, "true") is not null;
    }

    private Typed<long>? Integer(SyntaxNode v, string field, string node, long min, long max, string rule = "SY-54")
    {
        var s = Scalar(v, field, node, FieldForm.Name, "an integer");
        if (s is null)
        {
            return null;
        }

        if (Grammars.WholeNumber(s.Text, min, max, out var value, rule) is { } error)
        {
            _errors.Value(error.Rule, s.Position, node, $"{field}: {error.Message}");
            return null;
        }

        return new Typed<long>(s, value);
    }

    private Typed<decimal>? Decimal(SyntaxNode v, string field, string node, decimal min, decimal max, bool minExclusive)
    {
        var s = Scalar(v, field, node, FieldForm.Name, "a decimal");
        if (s is null)
        {
            return null;
        }

        if (Grammars.DecimalNumber(s.Text, min, max, minExclusive, out var value) is { } error)
        {
            _errors.Value(error.Rule, s.Position, node, $"{field}: {error.Message}");
            return null;
        }

        return new Typed<decimal>(s, value);
    }

    private Typed<TimeSpan>? Duration(SyntaxNode v, string field, string node, bool zeroAllowed)
    {
        var s = Scalar(v, field, node, FieldForm.Name, "a duration such as 30s");
        if (s is null)
        {
            return null;
        }

        if (Grammars.Duration(s.Text, zeroAllowed, out var value) is { } error)
        {
            _errors.Value(error.Rule, s.Position, node, $"{field}: {error.Message}", error.Fix is null ? null : $"{field}: {error.Fix}");
            return null;
        }

        return new Typed<TimeSpan>(s, value);
    }

    /// <summary>An enumeration (SY-88); for fields that also take booleans, a misspelt boolean is SY-53.</summary>
    private Typed<T>? Enum<T>(SyntaxNode v, string field, string node, (string Text, T Value)[] values, bool booleanLike = false)
    {
        var s = Scalar(v, field, node, FieldForm.Name, "one of " + string.Join(", ", values.Select(x => x.Text)));
        if (s is null)
        {
            return null;
        }

        foreach (var (text, value) in values)
        {
            if (s.Text == text)
            {
                return new Typed<T>(s, value);
            }
        }

        if (booleanLike && Grammars.Boolean(s.Text, out _) is { Fix: { } fix })
        {
            _errors.Value("SY-53", s.Position, node, $"{field}: '{s.Text}' isn't a boolean: write {fix}", $"{field}: {fix}");
        }
        else
        {
            _errors.Value("SY-88", s.Position, node, $"{field}: '{s.Text}' isn't one of {string.Join(", ", values.Select(x => x.Text))}");
        }

        return null;
    }

    private List<Typed<long>>? ExitCodes(SyntaxNode v, string field, string node) =>
        Items(v, field, node, allowEmpty: false, item => Integer(item, field, node, 0, 4_294_967_295L, "SY-96"));

    private List<PlanScalar>? Strings(SyntaxNode v, string field, string node) =>
        Items(v, field, node, allowEmpty: false, item => Scalar(item, field, node, FieldForm.Literal, "a string"));

    private List<PlanScalar>? Names(SyntaxNode v, string field, string node, Func<string, GrammarError?> grammar) =>
        Items(v, field, node, allowEmpty: false, item => Name(item, field, node, grammar));

    /// <summary>
    /// A value that may be a literal or a whole-value reference (SY-77). A reference kind the field doesn't take, and a
    /// secret literal, are kept and recorded as misplaced for RF-9 and RF-19.
    /// </summary>
    private PlanValue? Value(SyntaxNode v, string field, string node, FieldForm form, bool allowVar = false, bool allowFromEnv = false,
        bool allowFile = false, bool nonEmpty = false, bool record = true)
    {
        if (v is SyntaxScalar)
        {
            return Literal(Scalar(v, field, node, form, "a string", nonEmpty));
        }

        if (v is not SyntaxMapping m)
        {
            if (v is SyntaxSequence)
            {
                _errors.Value("SY-88", v.Start, node, $"{field}: takes a string{(allowVar ? " or { var: X }" : "")}, not a list");
            }

            return null;
        }

        if (m.Entries.Count == 0)
        {
            _errors.Value("SY-22", m.Start, node, $"{field}: an empty mapping isn't a value");
            return null;
        }

        PlanValue? value;
        var misplaced = false;
        if (m.Has("var"))
        {
            value = Reference(m, ReferenceKind.Var, node);
            misplaced = !allowVar;
        }
        else if (m.Has("from_env"))
        {
            value = Reference(m, ReferenceKind.FromEnv, node);
            misplaced = !allowFromEnv;
        }
        else if (allowFile && m.Has("file"))
        {
            PlanValue? path = null;
            var holder = new IndexSpec(m.Start, node);
            Fields(m, MappingShape.StdinFile, node, holder,
                (_, pv) => path = Value(pv, field + ".file", node, FieldForm.Reference, allowVar: true, nonEmpty: true));
            value = new FileValue(m, path);
        }
        else if (HasSecret(m))
        {
            value = new SecretLiteralValue(m);
            misplaced = true;
        }
        else
        {
            _errors.Value("SY-88", m.Start, node, $"{field}: takes a string{(allowVar ? " or { var: X }" : "")}, not this mapping");
            return null;
        }

        if (misplaced && record)
        {
            Misplace(value, field, node, form);
        }

        return value;
    }

    private ReferenceValue Reference(SyntaxMapping m, ReferenceKind kind, string node)
    {
        var keyName = kind == ReferenceKind.Var ? "var" : "from_env";
        var entry = m.Entries.First(e => e.Key.Value == keyName);
        PlanScalar? name = null;
        Typed<bool>? secret = null;
        var holder = new IndexSpec(m.Start, node);
        Fields(m, kind == ReferenceKind.Var ? MappingShape.VarReference : MappingShape.FromEnvReference, node, holder, (k, value) =>
        {
            if (k.Name == "secret")
            {
                secret = Boolean(value, "secret", node);
            }
            else
            {
                name = Name(value, keyName, node, kind == ReferenceKind.Var ? Grammars.VariableName : Grammars.EnvironmentName);
            }
        });
        return new ReferenceValue(m, kind, new PlanKey(keyName, entry.Key.Start), name, secret);
    }

    private void Misplace(PlanValue? value, string field, string node, FieldForm form)
    {
        if (value is not null)
        {
            _misplaced.Add(new MisplacedReference(value, field, node, form));
        }
    }

    private static bool IsReference(SyntaxMapping m) => m.Has("var") || m.Has("from_env");

    private static bool HasSecret(SyntaxMapping m) => m.Has("secret");

    // ---- collections ----------------------------------------------------------------------------------------------

    /// <summary>A fixed-shape mapping: anything else is SY-88; an empty one SY-22.</summary>
    private SyntaxMapping? Mapping(SyntaxNode v, string field, string node, string what)
    {
        switch (v)
        {
            case SyntaxMapping { Entries.Count: 0 }:
                _errors.Value("SY-22", v.Start, node, $"{field}: an empty mapping isn't allowed: {what}");
                return null;
            case SyntaxMapping m:
                return m;
            case SyntaxAlias:
                return null;
            default:
                _errors.Value("SY-88", v.Start, node, $"{field}: takes {what}");
                return null;
        }
    }

    /// <summary>
    /// The entries of a fixed-shape mapping: unknown keys are reported (SY-19, SY-30) and skipped, empty values are SY-20 at
    /// their key, aliases are skipped; every other entry goes to <paramref name="field"/>.
    /// </summary>
    private void Fields(SyntaxMapping m, MappingShape shape, string node, MappingObject target, Action<PlanKey, SyntaxNode> field)
    {
        foreach (var (k, v) in m.Entries)
        {
            if (!KeyShapes.IsKey(shape, k.Value))
            {
                Unknown(shape, k, node);
                continue;
            }

            var key = new PlanKey(k.Value, k.Start);
            target.AddKey(key);
            if (v is SyntaxAlias)
            {
                continue;
            }

            if (IsEmpty(v))
            {
                EmptyValue(key, node);
                continue;
            }

            field(key, v);
        }
    }

    /// <summary>
    /// A name-keyed mapping (<c>env</c>, <c>set</c>, <c>inputs</c>, capture fields …): non-empty (SY-22), each key matching
    /// <paramref name="grammar"/> (SY-52), each value present (SY-20).
    /// </summary>
    private List<T>? Named<T>(SyntaxNode v, PlanKey key, string field, string node, Func<string, GrammarError?> grammar,
        Func<PlanKey, bool, SyntaxNode?, T?> entry)
    {
        var m = Mapping(v, field, node, "a mapping of names to values");
        if (m is null)
        {
            return null;
        }

        var result = new List<T>();
        foreach (var (k, value) in m.Entries)
        {
            var name = new PlanKey(k.Value, k.Start);
            var error = grammar(k.Value);
            if (error is not null)
            {
                _errors.Value(error.Rule, k.Start, node, $"{field}: {error.Message}");
            }

            if (IsEmpty(value))
            {
                EmptyValue(name, node);
                continue;
            }

            if (entry(name, error is null, value is SyntaxAlias ? null : value) is { } built)
            {
                result.Add(built);
            }
        }

        _ = key;
        return result;
    }

    /// <summary>A list field: anything else is SY-88; an empty list is RF-21 unless allowed; empty items are SY-20 at their dash.</summary>
    private List<T>? Items<T>(SyntaxNode v, string field, string node, bool allowEmpty, Func<SyntaxNode, T?> item)
    {
        if (v is not SyntaxSequence sequence)
        {
            if (v is SyntaxMapping m && (IsReference(m) || HasSecret(m)))
            {
                Misplace(Value(v, field, node, FieldForm.Literal, record: false), field, node, FieldForm.Literal);
            }
            else if (v is not SyntaxAlias)
            {
                _errors.Value("SY-88", v.Start, node, $"{field}: takes a list");
            }

            return null;
        }

        if (sequence.Items.Count == 0 && !allowEmpty)
        {
            _errors.Value("RF-21", v.Start, node, $"{field}: an empty list isn't allowed here; leave the key out instead");
            return null;
        }

        var result = new List<T>();
        foreach (var entry in sequence.Items)
        {
            if (entry is SyntaxAlias)
            {
                continue;
            }

            if (IsEmpty(entry))
            {
                _errors.Value("SY-20", entry.Dash ?? entry.Start, node,
                    $"{field}: a list item with no value: an empty value is never unset; remove the dash, or write ''");
                continue;
            }

            if (item(entry) is { } built)
            {
                result.Add(built);
            }
        }

        return result;
    }

    private void Require(MappingObject target, SyntaxMapping m, string key, string what, string node)
    {
        if (!target.Has(key))
        {
            _errors.Invalid("SY-88", m.Start, node, $"{what} needs {key}");
        }
    }

    private static bool IsEmpty(SyntaxNode v) => v is SyntaxScalar { IsEmptyValue: true };

    private void EmptyValue(PlanKey key, string node) =>
        _errors.Value("SY-20", key.Position, node,
            $"{key.Name}: has no value: an empty value is never unset; remove the key, or write '' where an empty string is allowed");

    private void Unknown(MappingShape shape, SyntaxScalar key, string node)
    {
        var (suggestion, hint) = KeyShapes.Suggest(shape, key.Value);
        var where = shape switch
        {
            MappingShape.Root => "at the root of a plan",
            MappingShape.Node => "in a node",
            _ => $"in {Article(shape)}",
        };
        var message = $"unknown key '{key.Value}' {where}";
        string? fix = null;
        if (shape == MappingShape.Input)
        {
            switch (key.Value)
            {
                case "secret":
                    message = "an input can't be secret: pass secrets through the environment, as "
                        + "env: { X: { from_env: X, secret: true } }";
                    fix = "env: { X: { from_env: X, secret: true } }";
                    break;
                case "required":
                    message = "inputs have no required: key: an input without default: is required";
                    break;
                case "type":
                    message = "inputs have no type: key: every input is a string; constrain it with in: or matches:";
                    break;
            }
        }

        if (fix is null && suggestion is not null)
        {
            message += $"; did you mean {hint ?? suggestion}?";
        }

        _errors.UnknownKey(key.Start, node, message, suggestion, fix);
    }

    private static string Article(MappingShape shape) => shape switch
    {
        MappingShape.Input => "an input",
        MappingShape.Defaults => "defaults:",
        MappingShape.Condition => "a condition",
        MappingShape.Probe => "a probe",
        MappingShape.For => "for:",
        MappingShape.ForItems => "for.items",
        MappingShape.ForLines => "for.lines",
        MappingShape.Lock => "lock:",
        MappingShape.Tempdir => "tempdir:",
        MappingShape.Container => "container:",
        MappingShape.Mount => "a mount",
        MappingShape.Service => "a service",
        MappingShape.Backoff => "backoff:",
        MappingShape.Index => "index:",
        MappingShape.Carry => "a carry entry",
        MappingShape.VarReference => "a { var: X } reference",
        MappingShape.FromEnvReference => "a { from_env: NAME } reference",
        _ => "stdin: { file: P }",
    };

    private static int IndexOf(IReadOnlyList<string> list, string value)
    {
        for (var i = 0; i < list.Count; i++)
        {
            if (list[i] == value)
            {
                return i;
            }
        }

        return -1;
    }
}
