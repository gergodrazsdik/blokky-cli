using Blokky.Core.Ast;

namespace Blokky.Core.Validation.Rules.Reference;

/// <summary>
/// The rules of loops (reference.md §5.7, §5.8): <c>as</c> for scalar <c>for</c> sources and not for <c>matrix</c>
/// (RF-18), <c>for</c> options only with their source (RF-25), and <c>carry</c>/<c>collect</c> naming a capture bound by the
/// node or in its body (RF-30; where they may appear at all is <see cref="ValidityMatrixRule"/>'s).
/// </summary>
public sealed class LoopRule : IPlanRule
{
    private static readonly string[] ScalarSources = ["each", "count", "glob", "items", "lines"];

    /// <inheritdoc/>
    public IReadOnlyList<string> Rules { get; } = ["RF-18", "RF-25", "RF-30"];

    /// <inheritdoc/>
    public void Check(PlanContext context, PlanErrorSink errors)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(errors);
        var tree = PlanTree.Build(context.Ast);
        Bindings? bindings = null;
        for (var i = 0; i < tree.Nodes.Count; i++)
        {
            var node = tree.Nodes[i];
            if (node.For is { } spec && node.Key("for") is { } forKey)
            {
                For(node, spec, forKey, errors);
            }

            if (node.Has("carry") || node.Has("collect"))
            {
                bindings ??= Bindings.Of(tree);
                Names(node, i, tree.Ends[i], bindings, errors);
            }
        }
    }

    private static void For(PlanNode node, ForSpec spec, PlanKey forKey, PlanErrorSink errors)
    {
        if (spec.Has("matrix"))
        {
            if (spec.Key("as") is { } asKey)
            {
                errors.Invalid("RF-18", asKey.Position, node.Path,
                    "as: isn't valid with matrix: each matrix key is bound as a variable, and the index is MATRIX_INDEX", "remove as:");
            }
        }
        else if (!spec.Has("as") && ScalarSources.Any(spec.Has))
        {
            errors.Invalid("RF-18", forKey.Position, node.Path, "for: needs as: to name the variable that holds each item",
                "add as: NAME to the for: mapping");
        }

        Option(node, spec, "fields", "items", errors);
        Option(node, spec, "keep_blank", "lines", errors);
        Option(node, spec, "allow_empty", "glob", errors);
    }

    private static void Option(PlanNode node, ForSpec spec, string option, string source, PlanErrorSink errors)
    {
        if (spec.Key(option) is { } key && !spec.Has(source))
        {
            errors.Invalid("RF-25", key.Position, node.Path, $"{option}: is valid only with the {source} source",
                $"remove {option}:");
        }
    }

    private static void Names(PlanNode node, int start, int end, Bindings bindings, PlanErrorSink errors)
    {
        // Where carry and collect may appear is the matrix rule's (RF-30 too): only names on a valid placement are checked.
        var repetition = node.Keys.FirstOrDefault(k => Array.IndexOf(KindTables.Repetitions, k.Name) >= 0)?.Name;
        var bindsAny = bindings.Any(start, end);
        if (node.Key("carry") is { } carryKey && repetition is "while" or "until")
        {
            foreach (var entry in node.Carry ?? [])
            {
                if (entry.From is { } from && !bindings.Has(from.Text, start, end))
                {
                    Unknown(node, "carry", bindsAny ? from.Position : carryKey.Position, from.Text, errors);
                }
            }
        }

        if (node.Key("collect") is { } collectKey && repetition is "for" or "while" or "until")
        {
            foreach (var entry in node.Collect ?? [])
            {
                if (entry.CaptureValid && !bindings.Has(entry.Capture.Name, start, end))
                {
                    Unknown(node, "collect", bindsAny ? entry.Capture.Position : collectKey.Position, entry.Capture.Name, errors);
                }
            }
        }
    }

    private static void Unknown(PlanNode node, string field, Reading.PlanPosition at, string name, PlanErrorSink errors) =>
        errors.Invalid("RF-30", at, node.Path,
            $"{field}: names {name}, but neither this node nor its body binds a capture of that name",
            "name a capture bound by the node's capture: or by a node in its body");

    /// <summary>The names bound by captures and <c>set</c>, by node index: answers "is X bound in this subtree" in O(log n).</summary>
    private sealed class Bindings
    {
        private readonly Dictionary<string, List<int>> _byName = new(StringComparer.Ordinal);
        private readonly List<int> _all = [];

        public static Bindings Of(PlanTree tree)
        {
            var bindings = new Bindings();
            for (var i = 0; i < tree.Nodes.Count; i++)
            {
                var node = tree.Nodes[i];
                if (node.Capture?.Name is { } name)
                {
                    bindings.Add(name.Text, i);
                }

                foreach (var field in node.Capture?.Fields ?? [])
                {
                    bindings.Add(field.Name.Name, i);
                }

                if (node.CaptureFile is { } file)
                {
                    bindings.Add(file.Text, i);
                }

                foreach (var entry in node.Set ?? [])
                {
                    bindings.Add(entry.Name.Name, i);
                }
            }

            return bindings;
        }

        /// <summary>Whether a node in [<paramref name="start"/>, <paramref name="end"/>) binds <paramref name="name"/>.</summary>
        public bool Has(string name, int start, int end) => _byName.TryGetValue(name, out var at) && Within(at, start, end);

        /// <summary>Whether a node in [<paramref name="start"/>, <paramref name="end"/>) binds anything.</summary>
        public bool Any(int start, int end) => Within(_all, start, end);

        private static bool Within(List<int> sorted, int start, int end)
        {
            var i = sorted.BinarySearch(start);
            if (i < 0)
            {
                i = ~i;
            }

            return i < sorted.Count && sorted[i] < end;
        }

        private void Add(string name, int index)
        {
            if (!_byName.TryGetValue(name, out var list))
            {
                list = [];
                _byName[name] = list;
            }

            // Indexes are added in increasing order, so the lists stay sorted.
            list.Add(index);
            if (_all.Count == 0 || _all[^1] != index)
            {
                _all.Add(index);
            }
        }
    }
}
