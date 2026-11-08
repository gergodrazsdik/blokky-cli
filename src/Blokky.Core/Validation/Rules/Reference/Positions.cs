using Blokky.Core.Ast;

namespace Blokky.Core.Validation.Rules.Reference;

/// <summary>Position helpers for rules that relate two keys (SY-1: such an error is at the later key).</summary>
internal static class Positions
{
    /// <summary>The key of <paramref name="a"/> and <paramref name="b"/> that comes later in the text.</summary>
    public static PlanKey Later(PlanKey a, PlanKey b) =>
        (a.Position.Line, a.Position.Column).CompareTo((b.Position.Line, b.Position.Column)) > 0 ? a : b;
}
