using Blokky.Core.Ast;
using Blokky.Core.Syntax;

namespace Blokky.Core.Validation.Rules.Reference;

/// <summary>
/// RF-20 (reference.md §8, restated by SY-45 and VA-16): an input has at most one of <c>in</c> and <c>matches</c>, and its
/// <c>default</c> satisfies its constraint (SY-46) with condition semantics (VA-16): <c>in</c> by exact string match,
/// <c>matches</c> by an unanchored search with the portable regex subset (<see cref="PortableRegex"/>; inputs have no
/// <c>ignore_case</c>, so only a leading <c>(?i)</c> ignores case). A pattern outside the subset is RF-22's (the syntax
/// family), so the default isn't checked against it.
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

            if (input.Default is { } text && input.Matches is { } pattern && !pattern.Text.Contains("${", StringComparison.Ordinal)
                && PortableRegex.Compile(pattern.Text, ignoreCase: false, out _) is { } regex && !regex.IsMatch(text.Text))
            {
                errors.Invalid("RF-20", text.Position, input.Node,
                    $"the default of input {name}, '{text.Text}', doesn't match its matches: pattern '{pattern.Text}'",
                    "change the default, or the pattern, so that the pattern finds a match in it");
            }
        }
    }
}
