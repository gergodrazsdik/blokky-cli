using System.Globalization;

namespace Blokky.Core.Reading;

/// <summary>
/// A position in the plan as SY-1 defines it: lines from 1, ending at LF, CRLF or a lone CR; columns are 1-based code
/// points; a UTF-8 BOM isn't counted.
/// </summary>
/// <param name="Line">The 1-based line.</param>
/// <param name="Column">The 1-based column, in code points.</param>
public readonly record struct PlanPosition(int Line, int Column)
{
    /// <summary>Line 1, column 1: where errors about the whole text are located (SY-1).</summary>
    public static PlanPosition Start { get; } = new(1, 1);

    /// <summary>The error object's <c>pos</c> form, <c>line:column</c>.</summary>
    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"{Line}:{Column}");
}
