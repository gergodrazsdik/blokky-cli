using Blokky.Core.Ast;
using Blokky.Core.Reading;

namespace Blokky.Core.Validation;

/// <summary>What a static rule sees: the typed AST, plus the syntax tree and text for rules that need the plan as written.</summary>
/// <param name="Ast">The typed AST; schema-level errors were reported while it was built.</param>
/// <param name="Tree">The root mapping of the syntax tree.</param>
/// <param name="Text">The decoded plan text, without its BOM.</param>
/// <param name="Lines">The line map of <paramref name="Text"/>: positions in O(log n).</param>
/// <param name="EnvironmentVariable">
/// Reads the environment the run starts with, for what is resolved in validation (<c>from_env</c> in <c>vars</c>, VA-40);
/// null for the process environment.
/// </param>
public sealed record PlanContext(PlanAst Ast, SyntaxMapping Tree, string Text, LineMap Lines,
    Func<string, string?>? EnvironmentVariable = null);

/// <summary>
/// A family of static rules (reference.md §8 and the rules of syntax.md, variables.md, conditions.md that relate keys,
/// values or scopes), run on the AST after the schema-level checks. A rule reports through the sink and never throws for
/// a bad plan; it may see a partial AST (a value that was wrong is null) and must skip what is missing. Each family is
/// its own class, registered once in <see cref="PlanRules.All"/>.
/// </summary>
public interface IPlanRule
{
    /// <summary>The rule IDs the family reports (<c>RF-3</c>, <c>SH-7</c> …): for documentation and tests.</summary>
    IReadOnlyList<string> Rules { get; }

    /// <summary>Checks the plan and reports every violation into <paramref name="errors"/> (SY-3).</summary>
    void Check(PlanContext context, PlanErrorSink errors);
}

/// <summary>
/// The registered static rule families, run in this order. The extension point for T-0082 to T-0085 (architecture.md §1):
/// add one line in your task's section. Sections are separate so parallel PRs merge without conflicts.
/// </summary>
public static class PlanRules
{
    /// <summary>Every rule family of this build.</summary>
    public static IReadOnlyList<IPlanRule> All { get; } =
    [
        // T-0082: syntax (exec strings, regex subset, JSON paths, globs, image references).
        new Rules.Syntax.SyntaxRules(),

        // T-0083: variables and interpolation.
        new Rules.Variables.VariableRules(),

        // T-0084: conditions.
        new Rules.Conditions.ConditionRules(),

        // T-0085: reference and modifiers.
    ];
}
