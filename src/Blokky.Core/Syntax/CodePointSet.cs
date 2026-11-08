namespace Blokky.Core.Syntax;

/// <summary>An immutable set of Unicode code points: sorted, disjoint, non-adjacent inclusive ranges.</summary>
public sealed class CodePointSet
{
    /// <summary>The largest code point.</summary>
    public const int MaxCodePoint = 0x10FFFF;

    private readonly (int Lo, int Hi)[] _ranges;

    private CodePointSet((int Lo, int Hi)[] ranges) => _ranges = ranges;

    /// <summary>The empty set.</summary>
    public static CodePointSet Empty { get; } = new([]);

    /// <summary>Every code point.</summary>
    public static CodePointSet All { get; } = new([(0, MaxCodePoint)]);

    /// <summary>The ranges, sorted and disjoint.</summary>
    public ReadOnlySpan<(int Lo, int Hi)> Ranges => _ranges;

    /// <summary>Whether the set is empty.</summary>
    public bool IsEmpty => _ranges.Length == 0;

    /// <summary>A set of one code point.</summary>
    public static CodePointSet Of(int codePoint) => new([(codePoint, codePoint)]);

    /// <summary>A set of the given ranges, in any order, overlapping or not: O(n log n).</summary>
    public static CodePointSet FromRanges(IEnumerable<(int Lo, int Hi)> ranges)
    {
        ArgumentNullException.ThrowIfNull(ranges);
        var list = ranges.Where(r => r.Lo <= r.Hi).ToList();
        list.Sort();
        var merged = new List<(int Lo, int Hi)>(list.Count);
        foreach (var (lo, hi) in list)
        {
            if (merged.Count > 0 && lo <= merged[^1].Hi + 1)
            {
                merged[^1] = (merged[^1].Lo, Math.Max(merged[^1].Hi, hi));
            }
            else
            {
                merged.Add((lo, hi));
            }
        }

        return new([.. merged]);
    }

    /// <summary>Whether <paramref name="codePoint"/> is in the set: O(log n).</summary>
    public bool Contains(int codePoint)
    {
        int lo = 0, hi = _ranges.Length - 1;
        while (lo <= hi)
        {
            var mid = (lo + hi) >>> 1;
            if (codePoint < _ranges[mid].Lo)
            {
                hi = mid - 1;
            }
            else if (codePoint > _ranges[mid].Hi)
            {
                lo = mid + 1;
            }
            else
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The union with <paramref name="other"/>.</summary>
    public CodePointSet Union(CodePointSet other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return other.IsEmpty ? this : IsEmpty ? other : FromRanges([.. _ranges, .. other._ranges]);
    }

    /// <summary>Every code point not in the set.</summary>
    public CodePointSet Complement()
    {
        var result = new List<(int Lo, int Hi)>(_ranges.Length + 1);
        var next = 0;
        foreach (var (lo, hi) in _ranges)
        {
            if (lo > next)
            {
                result.Add((next, lo - 1));
            }

            next = hi + 1;
        }

        if (next <= MaxCodePoint)
        {
            result.Add((next, MaxCodePoint));
        }

        return new([.. result]);
    }

    /// <summary>The intersection with <paramref name="other"/>: a linear merge.</summary>
    public CodePointSet Intersect(CodePointSet other)
    {
        ArgumentNullException.ThrowIfNull(other);
        var result = new List<(int Lo, int Hi)>();
        int i = 0, j = 0;
        while (i < _ranges.Length && j < other._ranges.Length)
        {
            var (a, b) = _ranges[i];
            var (c, d) = other._ranges[j];
            var lo = Math.Max(a, c);
            var hi = Math.Min(b, d);
            if (lo <= hi)
            {
                result.Add((lo, hi));
            }

            if (b < d)
            {
                i++;
            }
            else
            {
                j++;
            }
        }

        return new([.. result]);
    }

    /// <summary>The set minus <paramref name="other"/>.</summary>
    public CodePointSet Except(CodePointSet other) => Intersect(other.Complement());

    /// <inheritdoc/>
    public override string ToString() =>
        string.Join(",", _ranges.ToArray().Select(r => r.Lo == r.Hi ? $"{r.Lo:X}" : $"{r.Lo:X}-{r.Hi:X}"));
}
