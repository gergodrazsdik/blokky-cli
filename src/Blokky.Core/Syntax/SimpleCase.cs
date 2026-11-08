namespace Blokky.Core.Syntax;

/// <summary>
/// The Unicode 16.0 simple uppercase mapping (UnicodeData.txt field 12) that CO-12 compares with, whatever the platform's
/// Unicode version. Never use <c>ToUpperInvariant</c>, <c>OrdinalIgnoreCase</c> or <c>RegexOptions.IgnoreCase</c> for the
/// language's case-insensitive comparisons: they leave out U+0131 and U+017F and treat the Kelvin sign as <c>k</c>.
/// </summary>
public static partial class SimpleCase
{
    private static readonly Lazy<CodePointSet> DomainSet = new(() =>
    {
        var pairs = Pairs;
        var ranges = new (int, int)[pairs.Length / 2];
        for (var i = 0; i < ranges.Length; i++)
        {
            ranges[i] = (pairs[2 * i], pairs[2 * i]);
        }

        return CodePointSet.FromRanges(ranges);
    });

    /// <summary>The simple uppercase mapping of <paramref name="codePoint"/> (itself if it has none): O(log n).</summary>
    public static int Upper(int codePoint)
    {
        var pairs = Pairs;
        int lo = 0, hi = (pairs.Length / 2) - 1;
        while (lo <= hi)
        {
            var mid = (lo + hi) >>> 1;
            var key = pairs[2 * mid];
            if (codePoint < key)
            {
                hi = mid - 1;
            }
            else if (codePoint > key)
            {
                lo = mid + 1;
            }
            else
            {
                return pairs[(2 * mid) + 1];
            }
        }

        return codePoint;
    }

    /// <summary>Whether two strings are equal ignoring case (CO-12): code point by code point, by simple uppercase mapping.</summary>
    public static bool EqualsIgnoreCase(string a, string b)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);
        var left = a.EnumerateRunes();
        var right = b.EnumerateRunes();
        while (true)
        {
            var hasLeft = left.MoveNext();
            var hasRight = right.MoveNext();
            if (hasLeft != hasRight)
            {
                return false;
            }

            if (!hasLeft)
            {
                return true;
            }

            if (Upper(left.Current.Value) != Upper(right.Current.Value))
            {
                return false;
            }
        }
    }

    /// <summary>
    /// <c>{c | Upper(c) ∈ Upper(S)}</c>: every code point equal to one of <paramref name="set"/> ignoring case (CO-12, the
    /// regex subset's <c>(?i)</c>). O(m log n) for the m mappings.
    /// </summary>
    public static CodePointSet Fold(CodePointSet set)
    {
        ArgumentNullException.ThrowIfNull(set);
        var domain = DomainSet.Value;
        var pairs = Pairs;

        // U = Upper(S): the code points without a mapping stay, the mapped ones are replaced by their uppercase.
        var upper = new List<(int, int)>();
        upper.AddRange(set.Except(domain).Ranges.ToArray());
        for (var i = 0; i < pairs.Length; i += 2)
        {
            if (set.Contains(pairs[i]))
            {
                upper.Add((pairs[i + 1], pairs[i + 1]));
            }
        }

        var u = CodePointSet.FromRanges(upper);

        // {c | Upper(c) ∈ U}: unmapped c with c ∈ U, and mapped c with Upper(c) ∈ U.
        var result = new List<(int, int)>();
        result.AddRange(u.Except(domain).Ranges.ToArray());
        for (var i = 0; i < pairs.Length; i += 2)
        {
            if (u.Contains(pairs[i + 1]))
            {
                result.Add((pairs[i], pairs[i]));
            }
        }

        return CodePointSet.FromRanges(result);
    }
}
