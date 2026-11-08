using Blokky.Core.Reading;

namespace Blokky.Core.Ast;

/// <summary>The node kinds (reference.md §2), in the schema's order.</summary>
public enum NodeKind
{
    /// <summary><c>exec</c>.</summary>
    Exec,

    /// <summary><c>script</c>.</summary>
    Script,

    /// <summary><c>do</c>.</summary>
    Do,

    /// <summary><c>all</c>.</summary>
    All,

    /// <summary><c>any</c>.</summary>
    Any,

    /// <summary><c>race</c>.</summary>
    Race,

    /// <summary><c>if</c>.</summary>
    If,

    /// <summary><c>sleep</c>.</summary>
    Sleep,

    /// <summary><c>log</c>.</summary>
    Log,

    /// <summary><c>fail</c>.</summary>
    Fail,

    /// <summary><c>assert</c>.</summary>
    Assert,

    /// <summary><c>set</c>.</summary>
    Set,

    /// <summary><c>return</c>.</summary>
    Return,
}

/// <summary>Shells (<c>shell</c>, <c>defaults.shell</c>).</summary>
public enum ShellName
{
    /// <summary><c>bash</c>.</summary>
    Bash,

    /// <summary><c>sh</c>.</summary>
    Sh,

    /// <summary><c>pwsh</c>.</summary>
    Pwsh,

    /// <summary><c>powershell</c>.</summary>
    Powershell,

    /// <summary><c>zsh</c>.</summary>
    Zsh,
}

/// <summary><c>level</c> of <c>log</c>.</summary>
public enum LogLevel
{
    /// <summary><c>debug</c>.</summary>
    Debug,

    /// <summary><c>info</c>.</summary>
    Info,

    /// <summary><c>warn</c>.</summary>
    Warn,
}

/// <summary>The leaf option <c>encoding</c>.</summary>
public enum OutputEncoding
{
    /// <summary><c>utf-8</c>.</summary>
    Utf8,

    /// <summary><c>utf-16le</c>.</summary>
    Utf16Le,

    /// <summary><c>utf-16be</c>.</summary>
    Utf16Be,

    /// <summary><c>latin1</c>.</summary>
    Latin1,
}

/// <summary>The leaf option <c>keep_descendants</c>.</summary>
public enum KeepDescendants
{
    /// <summary><c>false</c>.</summary>
    No,

    /// <summary><c>true</c>.</summary>
    Yes,

    /// <summary><c>detach</c>.</summary>
    Detach,
}

/// <summary><c>tempdir.remove</c>.</summary>
public enum TempdirRemove
{
    /// <summary><c>false</c>.</summary>
    No,

    /// <summary><c>true</c>.</summary>
    Yes,

    /// <summary><c>on_success</c>.</summary>
    OnSuccess,
}

/// <summary><c>on_body_failure</c>.</summary>
public enum BodyFailure
{
    /// <summary><c>stop</c>.</summary>
    Stop,

    /// <summary><c>continue</c>.</summary>
    Continue,
}

/// <summary>A service's <c>on_exit</c>.</summary>
public enum ServiceExit
{
    /// <summary><c>fail</c>.</summary>
    Fail,

    /// <summary><c>ignore</c>.</summary>
    Ignore,
}

/// <summary>An AST object built from a fixed-shape mapping: its position and the keys it has, for the static rules.</summary>
public abstract class MappingObject
{
    private readonly List<PlanKey> _keys = [];

    private protected MappingObject(PlanPosition position, string node)
    {
        Position = position;
        Node = node;
    }

    /// <summary>Where the mapping starts (SY-1: "the mapping that lacks it").</summary>
    public PlanPosition Position { get; }

    /// <summary>The path id of the node it belongs to.</summary>
    public string Node { get; }

    /// <summary>The language keys the mapping has, in document order (unknown keys and plan-only keys below the root aren't).</summary>
    public IReadOnlyList<PlanKey> Keys => _keys;

    /// <summary>The key <paramref name="name"/>, or null.</summary>
    public PlanKey? Key(string name) => _keys.Find(k => k.Name == name);

    /// <summary>Whether the mapping has <paramref name="name"/>.</summary>
    public bool Has(string name) => Key(name) is not null;

    internal void AddKey(PlanKey key) => _keys.Add(key);
}

/// <summary>The plan document (syntax.md §4): the plan-only keys and the root node, which shares the root mapping.</summary>
public sealed class PlanAst
{
    internal PlanAst(PlanNode root) => Root = root;

    /// <summary><c>blokky:</c>, if it is a scalar.</summary>
    public PlanScalar? Version { get; internal set; }

    /// <summary>The supported version <see cref="Version"/> names, or null if missing, malformed or unsupported.</summary>
    public string? SpecVersion { get; internal set; }

    /// <summary><c>inputs:</c>, in declaration order.</summary>
    public IReadOnlyList<InputDecl>? Inputs { get; internal set; }

    /// <summary><c>vars:</c>, in order.</summary>
    public IReadOnlyList<NamedValue>? Vars { get; internal set; }

    /// <summary><c>outputs:</c>.</summary>
    public IReadOnlyList<PlanScalar>? Outputs { get; internal set; }

    /// <summary><c>defaults:</c>.</summary>
    public DefaultsSpec? Defaults { get; internal set; }

    /// <summary>The root node (path id <c>/</c>); its <see cref="MappingObject.Keys"/> include the plan-only keys.</summary>
    public PlanNode Root { get; }

    /// <summary>Every scalar value the AST holds, in document order, with its field form (for RF-9 and interpolation).</summary>
    public IReadOnlyList<PlanScalar> Scalars { get; internal set; } = [];

    /// <summary>References and secret literals in fields that don't take them (RF-9, RF-19): not reported by the builder.</summary>
    public IReadOnlyList<MisplacedReference> MisplacedReferences { get; internal set; } = [];
}

/// <summary>An input declaration (syntax.md §5).</summary>
public sealed class InputDecl(PlanKey name, bool nameValid, PlanPosition position) : MappingObject(position, "/")
{
    /// <summary>The input's name.</summary>
    public PlanKey Name { get; } = name;

    /// <summary>Whether the name matches the variable-name grammar (SY-52).</summary>
    public bool NameValid { get; } = nameValid;

    /// <summary><c>default</c>.</summary>
    public PlanScalar? Default { get; internal set; }

    /// <summary><c>description</c>.</summary>
    public PlanScalar? Description { get; internal set; }

    /// <summary><c>in</c>.</summary>
    public IReadOnlyList<PlanScalar>? In { get; internal set; }

    /// <summary><c>matches</c>: a regex (its subset is RF-22's).</summary>
    public PlanScalar? Matches { get; internal set; }
}

/// <summary><c>defaults:</c> (SY-37).</summary>
public sealed class DefaultsSpec(PlanPosition position) : MappingObject(position, "/")
{
    /// <summary><c>shell</c>.</summary>
    public Typed<ShellName>? Shell { get; internal set; }

    /// <summary><c>leaf_timeout</c>.</summary>
    public Typed<TimeSpan>? LeafTimeout { get; internal set; }
}

/// <summary>An <c>exec</c> value: the string form or an argv list (reference.md §2.1).</summary>
public sealed class ExecSpec
{
    internal ExecSpec(PlanPosition position, PlanScalar? command, IReadOnlyList<PlanValue?>? argv)
    {
        Position = position;
        Command = command;
        Argv = argv;
    }

    /// <summary>Where the value starts.</summary>
    public PlanPosition Position { get; }

    /// <summary>The string form, split by the rules of shells.md (SH-7 is the syntax rule family's).</summary>
    public PlanScalar? Command { get; }

    /// <summary>The argv list: literals and <c>{ var: X }</c> (null entries were invalid).</summary>
    public IReadOnlyList<PlanValue?>? Argv { get; }

    /// <summary>Whether this is the string form.</summary>
    public bool IsString => Command is not null;
}

/// <summary>A node (reference.md §1): every key a node may carry, whatever its kind, so the static rules see misplaced keys.</summary>
public sealed class PlanNode : MappingObject
{
    internal PlanNode(PlanPosition position, string path)
        : base(position, path) => Path = path;

    /// <summary>The path id (<c>/</c>, <c>/do/2</c>, <c>/do/3/then/1</c>).</summary>
    public string Path { get; }

    /// <summary>The node's kind: its first kind key in document order; null if it has none (RF-1).</summary>
    public NodeKind? Kind { get; internal set; }

    /// <summary>The kind key.</summary>
    public PlanKey? KindKey { get; internal set; }

    // Kinds: each holds its value whether or not it is the node's kind, so a second kind's value is checked too.

    /// <summary><c>exec</c>.</summary>
    public ExecSpec? Exec { get; internal set; }

    /// <summary><c>script</c>.</summary>
    public PlanScalar? Script { get; internal set; }

    /// <summary><c>do</c>.</summary>
    public IReadOnlyList<PlanNode>? Do { get; internal set; }

    /// <summary><c>all</c>.</summary>
    public IReadOnlyList<PlanNode>? All { get; internal set; }

    /// <summary><c>any</c>.</summary>
    public IReadOnlyList<PlanNode>? Any { get; internal set; }

    /// <summary><c>race</c>.</summary>
    public IReadOnlyList<PlanNode>? Race { get; internal set; }

    /// <summary><c>if</c>.</summary>
    public Condition? If { get; internal set; }

    /// <summary><c>sleep</c> (<c>0s</c> allowed).</summary>
    public Typed<TimeSpan>? Sleep { get; internal set; }

    /// <summary><c>log</c>.</summary>
    public PlanScalar? Log { get; internal set; }

    /// <summary><c>fail</c>.</summary>
    public PlanScalar? Fail { get; internal set; }

    /// <summary><c>assert</c>.</summary>
    public Condition? Assert { get; internal set; }

    /// <summary><c>set</c>, in order.</summary>
    public IReadOnlyList<NamedValue>? Set { get; internal set; }

    /// <summary><c>return</c>.</summary>
    public PlanScalar? Return { get; internal set; }

    // Companions.

    /// <summary><c>shell</c>.</summary>
    public Typed<ShellName>? Shell { get; internal set; }

    /// <summary><c>then</c>.</summary>
    public IReadOnlyList<PlanNode>? Then { get; internal set; }

    /// <summary><c>else</c>.</summary>
    public IReadOnlyList<PlanNode>? Else { get; internal set; }

    /// <summary><c>level</c>.</summary>
    public Typed<LogLevel>? Level { get; internal set; }

    /// <summary><c>message</c>.</summary>
    public PlanScalar? Message { get; internal set; }

    /// <summary><c>code</c>.</summary>
    public PlanScalar? Code { get; internal set; }

    /// <summary><c>concurrency</c>.</summary>
    public Typed<long>? Concurrency { get; internal set; }

    /// <summary><c>fail_fast</c>.</summary>
    public Typed<bool>? FailFast { get; internal set; }

    // Leaf options.

    /// <summary><c>stdin</c>: a literal, <c>{ var: X }</c> or <c>{ file: P }</c>.</summary>
    public PlanValue? Stdin { get; internal set; }

    /// <summary><c>ok_exit</c>.</summary>
    public IReadOnlyList<Typed<long>>? OkExit { get; internal set; }

    /// <summary><c>stderr: merge</c>.</summary>
    public PlanScalar? Stderr { get; internal set; }

    /// <summary><c>encoding</c>.</summary>
    public Typed<OutputEncoding>? Encoding { get; internal set; }

    /// <summary><c>keep_descendants</c>.</summary>
    public Typed<KeepDescendants>? KeepDescendants { get; internal set; }

    /// <summary><c>capture</c>.</summary>
    public CaptureSpec? Capture { get; internal set; }

    /// <summary><c>capture_file</c>.</summary>
    public PlanScalar? CaptureFile { get; internal set; }

    /// <summary><c>secret</c>.</summary>
    public Typed<bool>? Secret { get; internal set; }

    // Modifiers.

    /// <summary><c>name</c>.</summary>
    public PlanScalar? Name { get; internal set; }

    /// <summary><c>when</c>.</summary>
    public Condition? When { get; internal set; }

    /// <summary><c>timeout</c>.</summary>
    public Typed<TimeSpan>? Timeout { get; internal set; }

    /// <summary><c>lock</c>.</summary>
    public LockSpec? Lock { get; internal set; }

    /// <summary><c>tempdir</c>.</summary>
    public TempdirSpec? Tempdir { get; internal set; }

    /// <summary><c>cwd</c>: a literal or <c>{ var: X }</c>.</summary>
    public PlanValue? Cwd { get; internal set; }

    /// <summary><c>env</c>, in order.</summary>
    public IReadOnlyList<NamedValue>? Env { get; internal set; }

    /// <summary><c>export</c>.</summary>
    public IReadOnlyList<PlanScalar>? Export { get; internal set; }

    /// <summary><c>container</c>.</summary>
    public ContainerSpec? Container { get; internal set; }

    /// <summary><c>services</c>.</summary>
    public IReadOnlyList<ServiceSpec>? Services { get; internal set; }

    /// <summary><c>finally</c>.</summary>
    public IReadOnlyList<PlanNode>? Finally { get; internal set; }

    /// <summary><c>finally_timeout</c>.</summary>
    public Typed<TimeSpan>? FinallyTimeout { get; internal set; }

    /// <summary><c>ignore_errors</c>.</summary>
    public Typed<bool>? IgnoreErrors { get; internal set; }

    // Repetition.

    /// <summary><c>retry</c>.</summary>
    public RetrySpec? Retry { get; internal set; }

    /// <summary><c>while</c>.</summary>
    public Condition? While { get; internal set; }

    /// <summary><c>until</c>.</summary>
    public Condition? Until { get; internal set; }

    /// <summary><c>for</c>.</summary>
    public ForSpec? For { get; internal set; }

    /// <summary><c>attempt_timeout</c>.</summary>
    public Typed<TimeSpan>? AttemptTimeout { get; internal set; }

    /// <summary><c>max</c>.</summary>
    public Typed<long>? Max { get; internal set; }

    /// <summary><c>every</c> (<c>0s</c> allowed).</summary>
    public Typed<TimeSpan>? Every { get; internal set; }

    /// <summary><c>backoff</c>.</summary>
    public BackoffSpec? Backoff { get; internal set; }

    /// <summary><c>jitter</c>.</summary>
    public Typed<decimal>? Jitter { get; internal set; }

    /// <summary><c>on_body_failure</c>.</summary>
    public Typed<BodyFailure>? OnBodyFailure { get; internal set; }

    /// <summary><c>index</c>.</summary>
    public IndexSpec? Index { get; internal set; }

    /// <summary><c>carry</c>.</summary>
    public IReadOnlyList<CarryEntry>? Carry { get; internal set; }

    /// <summary><c>collect</c>.</summary>
    public IReadOnlyList<CollectEntry>? Collect { get; internal set; }

    /// <summary>The child nodes in the body fields, in document order of the fields, with the field name.</summary>
    public IEnumerable<(string Field, IReadOnlyList<PlanNode> Nodes)> Bodies()
    {
        foreach (var key in Keys)
        {
            var list = key.Name switch
            {
                "do" => Do,
                "all" => All,
                "any" => Any,
                "race" => Race,
                "then" => Then,
                "else" => Else,
                "finally" => Finally,
                _ => null,
            };
            if (list is not null)
            {
                yield return (key.Name, list);
            }
        }
    }
}

/// <summary><c>capture</c>: one variable, or a mapping of variable names to JSON paths.</summary>
public sealed class CaptureSpec(PlanPosition position, PlanScalar? name, IReadOnlyList<CaptureField>? fields)
{
    /// <summary>Where the value starts.</summary>
    public PlanPosition Position { get; } = position;

    /// <summary><c>capture: NAME</c>.</summary>
    public PlanScalar? Name { get; } = name;

    /// <summary><c>capture: { NAME: .path }</c>.</summary>
    public IReadOnlyList<CaptureField>? Fields { get; } = fields;
}

/// <summary>A field capture or <c>for.fields</c> entry: a variable and a JSON path (its grammar is SY-63's).</summary>
/// <param name="Name">The variable.</param>
/// <param name="NameValid">Whether it matches SY-52.</param>
/// <param name="Path">The JSON path; null if it was invalid.</param>
public sealed record CaptureField(PlanKey Name, bool NameValid, PlanScalar? Path);

/// <summary><c>lock</c>: <c>NAME</c> or <c>{ name, wait }</c>.</summary>
public sealed class LockSpec(PlanPosition position, string node) : MappingObject(position, node)
{
    /// <summary>The lock name.</summary>
    public PlanScalar? Name { get; internal set; }

    /// <summary><c>wait</c> (<c>0s</c> allowed).</summary>
    public Typed<TimeSpan>? Wait { get; internal set; }
}

/// <summary><c>tempdir</c>: <c>true</c> or <c>{ as, prefix, remove }</c>.</summary>
public sealed class TempdirSpec(PlanPosition position, string node) : MappingObject(position, node)
{
    /// <summary><c>as</c>.</summary>
    public PlanScalar? As { get; internal set; }

    /// <summary><c>prefix</c>.</summary>
    public PlanScalar? Prefix { get; internal set; }

    /// <summary><c>remove</c>.</summary>
    public Typed<TempdirRemove>? Remove { get; internal set; }
}

/// <summary><c>container</c> (containers.md).</summary>
public sealed class ContainerSpec(PlanPosition position, string node) : MappingObject(position, node)
{
    /// <summary><c>image</c> (its OCI grammar is SY-103's).</summary>
    public PlanScalar? Image { get; internal set; }

    /// <summary><c>mounts</c> (an empty list is allowed).</summary>
    public IReadOnlyList<MountSpec>? Mounts { get; internal set; }

    /// <summary><c>env_pass</c>.</summary>
    public IReadOnlyList<PlanScalar>? EnvPass { get; internal set; }
}

/// <summary>A <c>container.mounts</c> entry.</summary>
public sealed class MountSpec(PlanPosition position, string node) : MappingObject(position, node)
{
    /// <summary><c>host</c>: a literal or <c>{ var: X }</c>.</summary>
    public PlanValue? Host { get; internal set; }

    /// <summary><c>path</c>.</summary>
    public PlanScalar? Path { get; internal set; }

    /// <summary><c>read_only</c>.</summary>
    public Typed<bool>? ReadOnly { get; internal set; }
}

/// <summary>A <c>services</c> entry (reference.md §4.9).</summary>
public sealed class ServiceSpec(PlanPosition position, string node) : MappingObject(position, node)
{
    /// <summary><c>name</c>.</summary>
    public PlanScalar? Name { get; internal set; }

    /// <summary><c>exec</c>.</summary>
    public ExecSpec? Exec { get; internal set; }

    /// <summary><c>script</c>.</summary>
    public PlanScalar? Script { get; internal set; }

    /// <summary><c>shell</c>.</summary>
    public Typed<ShellName>? Shell { get; internal set; }

    /// <summary><c>env</c>.</summary>
    public IReadOnlyList<NamedValue>? Env { get; internal set; }

    /// <summary><c>export</c>.</summary>
    public IReadOnlyList<PlanScalar>? Export { get; internal set; }

    /// <summary><c>cwd</c>.</summary>
    public PlanValue? Cwd { get; internal set; }

    /// <summary><c>ready</c>.</summary>
    public Condition? Ready { get; internal set; }

    /// <summary><c>ready_timeout</c>.</summary>
    public Typed<TimeSpan>? ReadyTimeout { get; internal set; }

    /// <summary><c>on_exit</c>.</summary>
    public Typed<ServiceExit>? OnExit { get; internal set; }
}

/// <summary><c>retry</c>: <c>true</c> or a condition.</summary>
public sealed class RetrySpec(PlanPosition position, Condition? condition)
{
    /// <summary>Where the value starts.</summary>
    public PlanPosition Position { get; } = position;

    /// <summary>The condition, or null for <c>retry: true</c>.</summary>
    public Condition? Condition { get; } = condition;
}

/// <summary><c>for</c> (reference.md §5.7).</summary>
public sealed class ForSpec(PlanPosition position, string node) : MappingObject(position, node)
{
    /// <summary><c>each</c>.</summary>
    public IReadOnlyList<PlanScalar>? Each { get; internal set; }

    /// <summary><c>count</c>.</summary>
    public Typed<long>? Count { get; internal set; }

    /// <summary><c>glob</c> (its grammar is SY-69's).</summary>
    public PlanScalar? Glob { get; internal set; }

    /// <summary><c>items.var</c>, when <c>items</c> is given.</summary>
    public PlanScalar? ItemsVar { get; internal set; }

    /// <summary><c>items.json</c>.</summary>
    public PlanScalar? ItemsJson { get; internal set; }

    /// <summary><c>lines.var</c>, when <c>lines</c> is given.</summary>
    public PlanScalar? LinesVar { get; internal set; }

    /// <summary><c>matrix</c>: variable names and their values, in key order.</summary>
    public IReadOnlyList<(PlanKey Name, IReadOnlyList<PlanScalar>? Values)>? Matrix { get; internal set; }

    /// <summary><c>as</c>.</summary>
    public PlanScalar? As { get; internal set; }

    /// <summary><c>concurrency</c>.</summary>
    public Typed<long>? Concurrency { get; internal set; }

    /// <summary><c>fail_fast</c>.</summary>
    public Typed<bool>? FailFast { get; internal set; }

    /// <summary><c>allow_empty</c>.</summary>
    public Typed<bool>? AllowEmpty { get; internal set; }

    /// <summary><c>max_items</c>.</summary>
    public Typed<long>? MaxItems { get; internal set; }

    /// <summary><c>fields</c>.</summary>
    public IReadOnlyList<CaptureField>? Fields { get; internal set; }

    /// <summary><c>keep_blank</c>.</summary>
    public Typed<bool>? KeepBlank { get; internal set; }
}

/// <summary><c>backoff</c>.</summary>
public sealed class BackoffSpec(PlanPosition position, string node) : MappingObject(position, node)
{
    /// <summary><c>factor</c>, 1 to 10.</summary>
    public Typed<decimal>? Factor { get; internal set; }

    /// <summary><c>max</c>.</summary>
    public Typed<TimeSpan>? Max { get; internal set; }
}

/// <summary><c>index</c>: <c>NAME</c> or <c>{ as, start }</c>.</summary>
public sealed class IndexSpec(PlanPosition position, string node) : MappingObject(position, node)
{
    /// <summary>The variable.</summary>
    public PlanScalar? As { get; internal set; }

    /// <summary><c>start</c>.</summary>
    public Typed<long>? Start { get; internal set; }
}

/// <summary>A <c>carry</c> entry: <c>NAME: CAPTURE</c> or <c>NAME: { from, initial }</c>.</summary>
/// <param name="Name">The carried variable.</param>
/// <param name="NameValid">Whether it matches SY-52.</param>
/// <param name="From">The capture it comes from.</param>
/// <param name="Initial">The first value, for the long form.</param>
public sealed record CarryEntry(PlanKey Name, bool NameValid, PlanScalar? From, PlanScalar? Initial);

/// <summary>A <c>collect</c> entry: <c>CAPTURE: LISTVAR</c>.</summary>
/// <param name="Capture">The capture collected.</param>
/// <param name="CaptureValid">Whether it matches SY-52.</param>
/// <param name="List">The list variable.</param>
public sealed record CollectEntry(PlanKey Capture, bool CaptureValid, PlanScalar? List);
