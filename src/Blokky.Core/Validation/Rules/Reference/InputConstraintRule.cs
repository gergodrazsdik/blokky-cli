using Blokky.Core.Ast;

namespace Blokky.Core.Validation.Rules.Reference;

/// <summary>
/// RF-20 (reference.md §8, restated by SY-45 and VA-16): an input has at most one of <c>in</c> and <c>matches</c>, and its
/// <c>default</c> satisfies its <c>in</c> list (an exact string match). Whether a default satisfies <c>matches</c> needs the
/// portable regex subset, which is the syntax rule family's (RF-22, T-0082); that part is pending on it.
/// </summary>
public sealed class InputConstraintRule : IPlanRule
{
    /// <inheritdoc/>
    public IReadOnlyList<string> Rules { get; } = ["RF-20"];

    /// <inheritdoc/>
    public void Check(PlanContext context, PlanErrorSink errors)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(errors);
        foreach (var input in context.Ast.Inputs ?? [])
        {
            var name = input.Name.Name;
            if (input.Key("in") is { } inKey && input.Key("matches") is { } matchesKey)
            {
                errors.Invalid("RF-20", Positions.Later(inKey, matchesKey).Position, input.Node,
                    $"input {name} has both in: and matches:; it takes at most one", "keep one constraint");
            }

            if (input.Default is { } value && input.In is { } list && !list.Any(item => item.Text == value.Text))
            {
                errors.Invalid("RF-20", value.Position, input.Node,
                    $"the default of input {name}, '{value.Text}', isn't one of its in: values",
                    "change the default to one of the in: values");
            }
        }
    }
}
