using Blokky.Core.Reading;

namespace Blokky.Core.Ast;

/// <summary>Condition subjects (conditions.md §2), in the schema's order.</summary>
public enum SubjectKind
{
    /// <summary><c>var</c>.</summary>
    Var,

    /// <summary><c>env</c>.</summary>
    Env,

    /// <summary><c>path</c>.</summary>
    Path,

    /// <summary><c>command</c>.</summary>
    Command,

    /// <summary><c>host</c>.</summary>
    Host,

    /// <summary><c>result</c>.</summary>
    Result,

    /// <summary><c>probe</c>.</summary>
    Probe,

    /// <summary><c>port</c>.</summary>
    Port,

    /// <summary><c>output</c>.</summary>
    Output,
}

/// <summary>Condition operators (conditions.md §3), in the schema's order.</summary>
public enum OperatorKind
{
    /// <summary><c>equals</c>.</summary>
    EqualsTo,

    /// <summary><c>not_equals</c>.</summary>
    NotEquals,

    /// <summary><c>in</c>.</summary>
    In,

    /// <summary><c>not_in</c>.</summary>
    NotIn,

    /// <summary><c>contains</c>.</summary>
    Contains,

    /// <summary><c>not_contains</c>.</summary>
    NotContains,

    /// <summary><c>matches</c>.</summary>
    Matches,

    /// <summary><c>not_matches</c>.</summary>
    NotMatches,

    /// <summary><c>empty</c>.</summary>
    Empty,

    /// <summary><c>not_empty</c>.</summary>
    NotEmpty,

    /// <summary><c>defined</c>.</summary>
    Defined,

    /// <summary><c>not_defined</c>.</summary>
    NotDefined,

    /// <summary><c>exists</c>.</summary>
    Exists,

    /// <summary><c>not_exists</c>.</summary>
    NotExists,

    /// <summary><c>is_file</c>.</summary>
    IsFile,

    /// <summary><c>is_dir</c>.</summary>
    IsDir,

    /// <summary><c>open</c>.</summary>
    Open,

    /// <summary><c>gt</c>.</summary>
    Gt,

    /// <summary><c>ge</c>.</summary>
    Ge,

    /// <summary><c>lt</c>.</summary>
    Lt,

    /// <summary><c>le</c>.</summary>
    Le,
}

/// <summary>A subject of a condition.</summary>
/// <param name="Kind">Which subject.</param>
/// <param name="Key">Its key.</param>
/// <param name="Value">Its value: a name, an enumeration value or a path (literal or <c>{ var: X }</c>); null for a probe
/// or an invalid value.</param>
/// <param name="Probe">The probe, for <see cref="SubjectKind.Probe"/>.</param>
public sealed record ConditionSubject(SubjectKind Kind, PlanKey Key, PlanValue? Value, Probe? Probe);

/// <summary>An operator of a condition.</summary>
/// <param name="Kind">Which operator.</param>
/// <param name="Key">Its key.</param>
/// <param name="Operand">Its operand (a literal; a reference here is recorded as misplaced); null for a list or an invalid
/// value.</param>
/// <param name="List">The list operand of <c>in</c> and <c>not_in</c>.</param>
public sealed record ConditionOperator(OperatorKind Kind, PlanKey Key, PlanValue? Operand, IReadOnlyList<PlanScalar>? List);

/// <summary>
/// A condition (conditions.md §1): its subjects, operators and modifiers as written, however many, so the conditions rule
/// family checks the shape (CO-1 to CO-4, CO-47).
/// </summary>
public sealed class Condition(PlanPosition position, string node) : MappingObject(position, node)
{
    private readonly List<ConditionSubject> _subjects = [];
    private readonly List<ConditionOperator> _operators = [];

    /// <summary>The subjects, in document order.</summary>
    public IReadOnlyList<ConditionSubject> Subjects => _subjects;

    /// <summary>The operators, in document order.</summary>
    public IReadOnlyList<ConditionOperator> Operators => _operators;

    /// <summary><c>json</c> (its grammar is SY-63's).</summary>
    public PlanScalar? Json { get; internal set; }

    /// <summary><c>ignore_case</c>.</summary>
    public Typed<bool>? IgnoreCase { get; internal set; }

    /// <summary><c>all</c>.</summary>
    public IReadOnlyList<Condition>? All { get; internal set; }

    /// <summary><c>any</c>.</summary>
    public IReadOnlyList<Condition>? Any { get; internal set; }

    /// <summary><c>not</c>.</summary>
    public Condition? Not { get; internal set; }

    /// <summary>
    /// A node kind key in the condition (<c>when: { exec: … }</c>): a node used as a condition, CO-4 at that key. The rest of
    /// the mapping isn't read.
    /// </summary>
    public PlanKey? NodeKindKey { get; internal set; }

    internal void Add(ConditionSubject subject) => _subjects.Add(subject);

    internal void Add(ConditionOperator op) => _operators.Add(op);
}

/// <summary>A probe (conditions.md §5).</summary>
public sealed class Probe(PlanPosition position, string node) : MappingObject(position, node)
{
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

    /// <summary><c>timeout</c>.</summary>
    public Typed<TimeSpan>? Timeout { get; internal set; }

    /// <summary><c>true_exit</c>.</summary>
    public IReadOnlyList<Typed<long>>? TrueExit { get; internal set; }

    /// <summary><c>false_exit</c>.</summary>
    public IReadOnlyList<Typed<long>>? FalseExit { get; internal set; }

    /// <summary><c>timeout_result</c>.</summary>
    public Typed<bool>? TimeoutResult { get; internal set; }
}
