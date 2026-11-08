using Blokky.Core.Reading;
using Blokky.Core.Reading.Yaml;

namespace Blokky.Core.Ast;

/// <summary>A node of the plan's YAML tree: a scalar (always a string, SY-23), a sequence, a mapping or an alias.</summary>
public abstract class SyntaxNode
{
    private protected SyntaxNode(PlanPosition start) => Start = start;

    /// <summary>Where errors about this value are located (SY-1), as <see cref="YamlEvent.Start"/>.</summary>
    public PlanPosition Start { get; }

    /// <summary>For an item of a block sequence, its <c>-</c>: where an empty item is located (SY-20); otherwise null.</summary>
    public PlanPosition? Dash { get; internal set; }
}

/// <summary>A scalar: its string as written, with its style.</summary>
public sealed class SyntaxScalar : SyntaxNode
{
    internal SyntaxScalar(PlanPosition start, string value, YamlScalarStyle style)
        : base(start)
    {
        Value = value;
        Style = style;
    }

    /// <summary>The scalar's string.</summary>
    public string Value { get; }

    /// <summary>How it is written.</summary>
    public YamlScalarStyle Style { get; }

    /// <summary>Nothing at all after the key or dash: an empty value, never "unset" (SY-20). <c>''</c> isn't one.</summary>
    public bool IsEmptyValue => Style == YamlScalarStyle.Plain && Value.Length == 0;
}

/// <summary>A sequence.</summary>
public sealed class SyntaxSequence : SyntaxNode
{
    internal SyntaxSequence(PlanPosition start, bool flow, IReadOnlyList<SyntaxNode> items)
        : base(start)
    {
        Flow = flow;
        Items = items;
    }

    /// <summary>Written in flow style (<c>[…]</c>).</summary>
    public bool Flow { get; }

    /// <summary>The items, in order.</summary>
    public IReadOnlyList<SyntaxNode> Items { get; }
}

/// <summary>A mapping entry: a key (a plain or quoted string, SY-17) and its value.</summary>
/// <param name="Key">The key.</param>
/// <param name="Value">The value.</param>
public sealed record SyntaxEntry(SyntaxScalar Key, SyntaxNode Value);

/// <summary>A mapping. Repeated keys (SY-18) and merge keys (SY-14) were reported by the reader and aren't in it.</summary>
public sealed class SyntaxMapping : SyntaxNode
{
    internal SyntaxMapping(PlanPosition start, bool flow, IReadOnlyList<SyntaxEntry> entries)
        : base(start)
    {
        Flow = flow;
        Entries = entries;
    }

    /// <summary>Written in flow style (<c>{…}</c>).</summary>
    public bool Flow { get; }

    /// <summary>The entries, in document order.</summary>
    public IReadOnlyList<SyntaxEntry> Entries { get; }

    /// <summary>The value of <paramref name="key"/>, or null. Linear: for the fixed-shape mappings of the language.</summary>
    public SyntaxNode? Get(string key)
    {
        foreach (var entry in Entries)
        {
            if (entry.Key.Value == key)
            {
                return entry.Value;
            }
        }

        return null;
    }

    /// <summary>Whether the mapping has <paramref name="key"/>.</summary>
    public bool Has(string key) => Get(key) is not null;
}

/// <summary>An alias (<c>*a</c>), already an error (SY-14): later checks skip it.</summary>
public sealed class SyntaxAlias : SyntaxNode
{
    internal SyntaxAlias(PlanPosition start)
        : base(start)
    {
    }
}

/// <summary>
/// Builds the tree of the plan's first YAML document from the reader's events, in one pass with an explicit stack. Only
/// for a plan the reader can continue with (<see cref="PlanReadResult.CanContinue"/>): every key is then a scalar or an
/// alias, and every key has a value event.
/// </summary>
public static class SyntaxTree
{
    /// <summary>The root mapping, or null if the first document's root isn't a mapping (the reader reported SY-32).</summary>
    public static SyntaxMapping? Build(IReadOnlyList<YamlEvent> events, IReadOnlyList<YamlMarker> markers)
    {
        ArgumentNullException.ThrowIfNull(events);
        ArgumentNullException.ThrowIfNull(markers);
        var dashes = markers.Where(m => m.Kind == YamlMarkerKind.SequenceEntry).Select(m => m.Position).ToArray();
        var stack = new Stack<Frame>();
        SyntaxNode? root = null;
        foreach (var e in events)
        {
            SyntaxNode? done = null;
            switch (e.Kind)
            {
                case YamlEventKind.DocumentEnd:
                    return root as SyntaxMapping;
                case YamlEventKind.MappingStart or YamlEventKind.SequenceStart:
                    stack.Push(new Frame(e.Kind == YamlEventKind.MappingStart, e.Start, e.Flow));
                    continue;
                case YamlEventKind.MappingEnd or YamlEventKind.SequenceEnd:
                    done = stack.Pop().Finish();
                    break;
                case YamlEventKind.Scalar:
                    done = new SyntaxScalar(e.Start, e.Value ?? "", e.Style);
                    break;
                case YamlEventKind.Alias:
                    done = new SyntaxAlias(e.Start);
                    break;
                default:
                    continue;
            }

            if (stack.Count == 0)
            {
                root = done;
                continue;
            }

            var parent = stack.Peek();
            if (!parent.IsMapping && !parent.Flow && done is SyntaxScalar { IsEmptyValue: true })
            {
                done.Dash = LastBefore(dashes, done.Start);
            }

            parent.Add(done);
        }

        return root as SyntaxMapping;
    }

    /// <summary>The last position in <paramref name="sorted"/> before <paramref name="at"/>; O(log n).</summary>
    private static PlanPosition? LastBefore(PlanPosition[] sorted, PlanPosition at)
    {
        int lo = 0, hi = sorted.Length;
        while (lo < hi)
        {
            var mid = (lo + hi) >>> 1;
            if (Compare(sorted[mid], at) < 0)
            {
                lo = mid + 1;
            }
            else
            {
                hi = mid;
            }
        }

        return lo == 0 ? null : sorted[lo - 1];
    }

    internal static int Compare(PlanPosition a, PlanPosition b) =>
        a.Line != b.Line ? a.Line.CompareTo(b.Line) : a.Column.CompareTo(b.Column);

    /// <summary>An open collection.</summary>
    private sealed class Frame(bool isMapping, PlanPosition start, bool flow)
    {
        private readonly List<SyntaxNode> _items = [];
        private readonly List<SyntaxEntry> _entries = [];
        private readonly HashSet<string> _keys = new(StringComparer.Ordinal);
        private SyntaxNode? _key;

        public bool IsMapping { get; } = isMapping;

        public bool Flow { get; } = flow;

        public void Add(SyntaxNode node)
        {
            if (!IsMapping)
            {
                _items.Add(node);
                return;
            }

            if (_key is null)
            {
                _key = node;
                return;
            }

            // Merge keys (SY-14), alias keys and repeated keys (SY-18) were reported by the reader; the first value of a key wins.
            if (_key is SyntaxScalar key && !(key.Style == YamlScalarStyle.Plain && key.Value == "<<") && _keys.Add(key.Value))
            {
                _entries.Add(new SyntaxEntry(key, node));
            }

            _key = null;
        }

        public SyntaxNode Finish() => IsMapping ? new SyntaxMapping(start, Flow, _entries) : new SyntaxSequence(start, Flow, _items);
    }
}
