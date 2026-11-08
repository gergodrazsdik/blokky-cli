using Blokky.Core.Ast;

namespace Blokky.Core.Syntax;

/// <summary>A <c>${…}</c> placeholder in a string (syntax.md §7.3).</summary>
/// <param name="Offset">Where <c>${</c> starts, in UTF-16 code units of the string.</param>
/// <param name="Length">The placeholder's length, <c>${</c> and <c>}</c> included (to the end of the string if unterminated).</param>
/// <param name="Content">The text between <c>${</c> and <c>}</c>, as written.</param>
/// <param name="Terminated">Whether a <c>}</c> closes it.</param>
public sealed record Placeholder(int Offset, int Length, string Content, bool Terminated)
{
    /// <summary>
    /// Whether this is a reference to a plan variable: terminated, with a variable name as its content (VA-4's grammar,
    /// <c>BLOKKY_</c> names included, which no plan can bind). Anything else is a malformed placeholder.
    /// </summary>
    public bool IsVariable => Terminated && InterpolationSyntax.IsVariableName(Content);
}

/// <summary>
/// The interpolation syntax of display strings and templates (syntax.md §7.3, SY-82 to SY-84, VA-56, VA-57): <c>${X}</c>
/// is plan variable <c>X</c>; <c>$${</c> is a literal <c>${</c> in every field. One linear scan, left to right. Shared by
/// the variables rule family (RF-9, RF-23, RF-27), the feature derivation (<c>interpolation</c>) and, later, the renderer.
/// </summary>
public static class InterpolationSyntax
{
    /// <summary>
    /// The placeholders in <paramref name="text"/>, in order. <c>$${</c> is an escape, never a placeholder (so <c>$$${X}</c>
    /// reads as <c>$</c>, <c>$${</c>, <c>X}</c>). A <c>${</c> without a closing <c>}</c> is one unterminated placeholder that
    /// runs to the end of the string.
    /// </summary>
    public static IReadOnlyList<Placeholder> Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        List<Placeholder>? found = null;
        var i = text.IndexOf('$', StringComparison.Ordinal);
        while (i >= 0 && i + 1 < text.Length)
        {
            if (text[i + 1] == '$' && i + 2 < text.Length && text[i + 2] == '{')
            {
                i = text.IndexOf('$', i + 3);
                continue;
            }

            if (text[i + 1] != '{')
            {
                i = text.IndexOf('$', i + 1);
                continue;
            }

            var close = text.IndexOf('}', i + 2);
            found ??= [];
            if (close < 0)
            {
                found.Add(new Placeholder(i, text.Length - i, text[(i + 2)..], Terminated: false));
                break;
            }

            found.Add(new Placeholder(i, close + 1 - i, text[(i + 2)..close], Terminated: true));
            i = text.IndexOf('$', close + 1);
        }

        return found ?? (IReadOnlyList<Placeholder>)[];
    }

    /// <summary>Whether <paramref name="text"/> has a <c>${</c> that isn't escaped as <c>$${</c>.</summary>
    public static bool HasPlaceholder(string text) => Parse(text).Count > 0;

    /// <summary>The plan variables <paramref name="text"/> interpolates, with repeats, in order.</summary>
    public static IEnumerable<string> Variables(string text) => Parse(text).Where(p => p.IsVariable).Select(p => p.Content);

    /// <summary>Whether <paramref name="scalar"/>'s field interpolates <c>${…}</c>: display strings and templates.</summary>
    public static bool Interpolates(PlanScalar scalar)
    {
        ArgumentNullException.ThrowIfNull(scalar);
        return scalar.Form is FieldForm.Display or FieldForm.Template;
    }

    /// <summary>
    /// Whether <paramref name="scalar"/> is literal text in which a <c>${</c> is RF-9 (SY-76): the literal and reference
    /// fields. Script text is the shell's own; names and enumerations already failed their grammar.
    /// </summary>
    public static bool IsLiteralText(PlanScalar scalar)
    {
        ArgumentNullException.ThrowIfNull(scalar);
        return scalar.Form is FieldForm.Literal or FieldForm.Reference;
    }

    /// <summary>The variable-name grammar, <c>^[A-Z][A-Z0-9_]*$</c> (VA-4).</summary>
    public static bool IsVariableName(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return Grammars.IsUpperName(text);
    }
}
