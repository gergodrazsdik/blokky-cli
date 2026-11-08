using Blokky.Core.Ast;
using Blokky.Core.Reading;

namespace Blokky.Core.Validation.Rules;

/// <summary>
/// Position helpers shared by the rule families: an error about two conflicting keys is at the later one (SY-1).
/// </summary>
internal static class Positions
{
    /// <summary><paramref name="a"/> if it comes later in the text than <paramref name="b"/>, else <paramref name="b"/>.</summary>
    public static PlanPosition Later(PlanPosition a, PlanPosition b) => SyntaxTree.Compare(a, b) > 0 ? a : b;

    /// <summary>The key of <paramref name="a"/> and <paramref name="b"/> that comes later in the text.</summary>
    public static PlanKey Later(PlanKey a, PlanKey b) => SyntaxTree.Compare(a.Position, b.Position) > 0 ? a : b;
}
