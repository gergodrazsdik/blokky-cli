using Blokky.Core.Reading;
using Blokky.Core.Reading.Yaml;

namespace Blokky.Core.Ast;

/// <summary>How a field takes its value (syntax.md §7.4, SY-88): what the static rules check a scalar against.</summary>
public enum FieldForm
{
    /// <summary>L: literal only; a <c>${</c> is RF-9 (SY-76), a reference too.</summary>
    Literal,

    /// <summary>R: literal or a whole-value reference; the scalar form is literal.</summary>
    Reference,

    /// <summary>
    /// <c>${}</c>: a display string with interpolation (<c>name</c>, the messages of <c>log</c>, <c>fail</c>, <c>assert</c>).
    /// </summary>
    Display,

    /// <summary>T: a template, the values of <c>set</c> and <c>vars</c>.</summary>
    Template,

    /// <summary>Executed text: <c>script</c> text, where <c>${…}</c> is the shell's own syntax (SY-76).</summary>
    ShellText,

    /// <summary>A name or enumeration value whose grammar the AST builder already checked.</summary>
    Name,
}

/// <summary>A mapping key the AST knows, with its position (SY-1: errors about a key are located there).</summary>
/// <param name="Name">The key.</param>
/// <param name="Position">Where it starts.</param>
public sealed record PlanKey(string Name, PlanPosition Position);

/// <summary>A scalar of the plan as written, with its position, field and form.</summary>
public sealed class PlanScalar
{
    internal PlanScalar(SyntaxScalar source, string field, FieldForm form, string node)
    {
        Text = source.Value;
        Style = source.Style;
        Position = source.Start;
        Field = field;
        Form = form;
        Node = node;
    }

    /// <summary>The string (SY-23).</summary>
    public string Text { get; }

    /// <summary>How it is written.</summary>
    public YamlScalarStyle Style { get; }

    /// <summary>Its position (SY-1).</summary>
    public PlanPosition Position { get; }

    /// <summary>The field it is the value of, as a dotted key path (<c>timeout</c>, <c>for.each</c>, <c>env</c>).</summary>
    public string Field { get; }

    /// <summary>How the field takes values.</summary>
    public FieldForm Form { get; }

    /// <summary>The path id of the node the scalar belongs to.</summary>
    public string Node { get; }

    /// <inheritdoc/>
    public override string ToString() => Text;
}

/// <summary>A scalar parsed by its field's grammar.</summary>
/// <typeparam name="T">The parsed type.</typeparam>
/// <param name="Source">The scalar as written.</param>
/// <param name="Value">The parsed value.</param>
public sealed record Typed<T>(PlanScalar Source, T Value)
{
    /// <summary>The scalar's position.</summary>
    public PlanPosition Position => Source.Position;
}

/// <summary>A value of a field that takes a literal or a whole-value reference (SY-77).</summary>
public abstract class PlanValue
{
    private protected PlanValue(PlanPosition position) => Position = position;

    /// <summary>Where the value starts.</summary>
    public PlanPosition Position { get; }
}

/// <summary>A literal (or template, in <c>set</c> and <c>vars</c>).</summary>
public sealed class LiteralValue(PlanScalar scalar) : PlanValue(scalar.Position)
{
    /// <summary>The scalar.</summary>
    public PlanScalar Scalar { get; } = scalar;
}

/// <summary>The kind of a whole-value reference.</summary>
public enum ReferenceKind
{
    /// <summary><c>{ var: X }</c>.</summary>
    Var,

    /// <summary><c>{ from_env: NAME }</c>.</summary>
    FromEnv,
}

/// <summary><c>{ var: X }</c> or <c>{ from_env: NAME }</c>, optionally <c>secret: true</c> (SY-77, SY-78).</summary>
public sealed class ReferenceValue : PlanValue
{
    internal ReferenceValue(SyntaxMapping source, ReferenceKind kind, PlanKey key, PlanScalar? name, Typed<bool>? secret)
        : base(source.Start)
    {
        Source = source;
        Kind = kind;
        Key = key;
        Name = name;
        Secret = secret;
    }

    /// <summary>The mapping.</summary>
    public SyntaxMapping Source { get; }

    /// <summary>Which reference.</summary>
    public ReferenceKind Kind { get; }

    /// <summary>The <c>var</c> or <c>from_env</c> key.</summary>
    public PlanKey Key { get; }

    /// <summary>The variable or environment variable name; null if it was invalid.</summary>
    public PlanScalar? Name { get; }

    /// <summary><c>secret</c>, if given.</summary>
    public Typed<bool>? Secret { get; }

    /// <summary>Whether it carries <c>secret: true</c>.</summary>
    public bool IsSecret => Secret?.Value == true;
}

/// <summary><c>stdin: { file: P }</c>: a host file's content (SY-77).</summary>
public sealed class FileValue(SyntaxMapping source, PlanValue? path) : PlanValue(source.Start)
{
    /// <summary>The path: a literal or <c>{ var: X }</c>; null if it was invalid.</summary>
    public PlanValue? Path { get; } = path;
}

/// <summary>
/// A mapping in a value position that is neither a literal nor a reference but carries <c>secret</c>: a secret literal
/// such as <c>{ value: abc, secret: true }</c>, which RF-19 reports (SY-91). The AST builder reports nothing for it.
/// </summary>
public sealed class SecretLiteralValue(SyntaxMapping source) : PlanValue(source.Start)
{
    /// <summary>The mapping.</summary>
    public SyntaxMapping Source { get; } = source;

    /// <summary>The <c>secret</c> key's position, where RF-19 is located.</summary>
    public PlanPosition SecretKey { get; } = source.Entries.First(e => e.Key.Value == "secret").Key.Start;
}

/// <summary>
/// A reference (or secret literal) in a field whose form doesn't take one: a literal field (RF-9, SY-80 for argv[0]), or
/// a reference kind the field doesn't accept (<c>from_env</c> in <c>cwd</c>). Recorded by the AST builder, which reports
/// nothing for it: RF-9 is the variables rule family's.
/// </summary>
/// <param name="Value">The reference.</param>
/// <param name="Field">The field, as a dotted key path.</param>
/// <param name="Node">The path id of the node.</param>
/// <param name="Form">The field's form.</param>
public sealed record MisplacedReference(PlanValue Value, string Field, string Node, FieldForm Form);

/// <summary>A named entry of a name-keyed mapping: <c>set</c>, <c>vars</c>, <c>env</c>.</summary>
/// <param name="Name">The name, with its position.</param>
/// <param name="NameValid">Whether the name matches its grammar (SY-52).</param>
/// <param name="Value">The value; null if it was invalid.</param>
public sealed record NamedValue(PlanKey Name, bool NameValid, PlanValue? Value);
