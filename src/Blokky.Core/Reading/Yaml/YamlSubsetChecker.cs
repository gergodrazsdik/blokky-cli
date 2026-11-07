using Blokky.Core.Errors;

namespace Blokky.Core.Reading.Yaml;

/// <summary>
/// The YAML subset (syntax.md §3.1) and the caps checked before a tree is built (§2.3), on Blokky's event stream: anchors,
/// aliases and merge keys (SY-14), tags and directives (SY-15), more than one document (SY-16), complex and empty keys
/// (SY-17), duplicate keys (SY-18), the nesting depth (SY-9) and a root that isn't a mapping (SY-32). Errors are
/// collected, not stop-at-first; a syntax error ends the stream, and the depth cap ends processing.
/// </summary>
/// <remarks>
/// Empty values (SY-20), empty collections (SY-21, SY-22) and unknown keys (SY-19) need the field a value belongs to, so
/// they are the AST's (T-0081): there, the targeted errors of SY-89 and SY-91 can replace them (SY-3).
/// </remarks>
public static class YamlSubsetChecker
{
    /// <summary>The nesting cap: levels of mappings and sequences, the root mapping included (SY-9).</summary>
    public const int MaxDepth = 128;

    /// <summary>
    /// Checks <paramref name="markers"/> and <paramref name="events"/>; consumes the events into <paramref name="seen"/>
    /// while it reads them. Returns the plan errors.
    /// </summary>
    public static IReadOnlyList<BlokkyError> Check(IReadOnlyList<YamlMarker> markers, IEnumerable<YamlEvent> events,
        ICollection<YamlEvent> seen)
    {
        ArgumentNullException.ThrowIfNull(markers);
        ArgumentNullException.ThrowIfNull(events);
        ArgumentNullException.ThrowIfNull(seen);
        var errors = new List<BlokkyError>();
        foreach (var marker in markers)
        {
            switch (marker.Kind)
            {
                case YamlMarkerKind.Directive:
                    errors.Add(Yaml("SY-15", marker.Position, "YAML directives (%YAML, %TAG) aren't allowed in a plan"));
                    break;
                case YamlMarkerKind.ExplicitKey:
                    errors.Add(Yaml("SY-17", marker.Position, "complex keys (the ? indicator) aren't allowed: write the key as a string"));
                    break;
                case YamlMarkerKind.EmptyKey:
                    errors.Add(Yaml("SY-17", marker.Position, "a key is missing before ':' (YAML would read it as a null key)"));
                    break;
            }
        }

        var explicitKeyLines = markers.Where(m => m.Kind == YamlMarkerKind.ExplicitKey).Select(m => m.Position.Line).ToHashSet();
        var frames = new Stack<Frame>();
        var documents = 0;
        var sawRoot = false;
        foreach (var e in events)
        {
            seen.Add(e);
            switch (e.Kind)
            {
                case YamlEventKind.Error:
                    errors.Add(Yaml(e.Rule, e.Start, e.Value ?? "invalid YAML"));
                    return errors;
                case YamlEventKind.DocumentStart:
                    documents++;
                    if (documents > 1)
                    {
                        errors.Add(Yaml("SY-16", e.Start, e.Implicit
                            ? "content after '...': a plan is one YAML document"
                            : "a second YAML document: a plan is one document, with at most one leading ---"));
                    }

                    break;
                case YamlEventKind.MappingEnd or YamlEventKind.SequenceEnd:
                    if (frames.Count > 0)
                    {
                        frames.Pop();
                    }

                    break;
            }

            if (!e.IsNode)
            {
                continue;
            }

            if (frames.Count == 0 && documents == 1 && !sawRoot)
            {
                sawRoot = true;
                CheckRoot(e, errors);
            }

            NodeProperties(e, errors);
            if (frames.TryPeek(out var parent) && parent.Keys is not null)
            {
                if (parent.ExpectKey)
                {
                    Key(e, parent.Keys, explicitKeyLines, errors);
                }

                parent.ExpectKey = !parent.ExpectKey;
            }

            if (e.Kind is YamlEventKind.MappingStart or YamlEventKind.SequenceStart)
            {
                if (frames.Count + 1 > MaxDepth)
                {
                    // SY-9: a cap ends processing; it is reported at the first node that is too deep (SY-1).
                    errors.Clear();
                    errors.Add(ReaderErrors.Create(ErrorCodes.PlanTooLarge, "SY-9", e.Start,
                        $"the plan is nested deeper than {MaxDepth} levels of mappings and sequences"));
                    return errors;
                }

                frames.Push(new Frame(e.Kind == YamlEventKind.MappingStart ? new HashSet<string>(StringComparer.Ordinal) : null));
            }
        }

        if (documents == 0)
        {
            errors.Add(ReaderErrors.Create(ErrorCodes.PlanValue, "SY-32", PlanPosition.Start,
                "the plan is empty: its root must be a mapping with blokky: and a root node"));
        }

        return errors;
    }

    /// <summary>SY-32: the root is a single mapping. An alias root is already an SY-14 error.</summary>
    private static void CheckRoot(YamlEvent root, List<BlokkyError> errors)
    {
        var message = root.Kind switch
        {
            YamlEventKind.SequenceStart => "the root of a plan is a list; it must be a mapping: put the list under do:",
            YamlEventKind.Scalar when root.Value is "" && root.Style == YamlScalarStyle.Plain =>
                "the plan is empty: its root must be a mapping with blokky: and a root node",
            YamlEventKind.Scalar => "the root of a plan is a scalar; it must be a mapping with blokky: and a root node",
            _ => null,
        };
        if (message is not null)
        {
            var at = root.Kind == YamlEventKind.Scalar && root.Value is "" ? PlanPosition.Start : root.Start;
            errors.Add(ReaderErrors.Create(ErrorCodes.PlanValue, "SY-32", at, message));
        }
    }

    /// <summary>SY-14 anchors and aliases, SY-15 tags: located where the node's text begins.</summary>
    private static void NodeProperties(YamlEvent e, List<BlokkyError> errors)
    {
        if (e.Kind == YamlEventKind.Alias)
        {
            errors.Add(Yaml("SY-14", e.Start, $"aliases (*{e.Value}) aren't allowed: write the value out"));
        }

        if (e.Anchor is not null)
        {
            errors.Add(Yaml("SY-14", e.NodeStart, $"anchors (&{e.Anchor}) aren't allowed: write each value out"));
        }

        if (e.Tag is not null)
        {
            errors.Add(Yaml("SY-15", e.NodeStart, "YAML tags aren't allowed: every scalar is a string, so remove the tag"));
        }
    }

    /// <summary>A mapping key: a plain or quoted string (SY-17), not a merge key (SY-14), not a repeat (SY-18).</summary>
    private static void Key(YamlEvent key, HashSet<string> keys, HashSet<int> explicitKeyLines, List<BlokkyError> errors)
    {
        switch (key.Kind)
        {
            case YamlEventKind.Alias:
                return; // reported as an alias
            case YamlEventKind.MappingStart or YamlEventKind.SequenceStart:
                if (!explicitKeyLines.Contains(key.Start.Line))
                {
                    errors.Add(Yaml("SY-17", key.Start, "a key must be a plain or quoted string, not a list or mapping"));
                }

                return;
        }

        if (key.Style == YamlScalarStyle.Plain && key.Value is "")
        {
            return; // an empty key: reported from the tokens (EmptyKey, ExplicitKey)
        }

        // A block scalar key needs the ? indicator, which is reported from the tokens.
        if (key.Style == YamlScalarStyle.Plain && key.Value == "<<")
        {
            errors.Add(Yaml("SY-14", key.Start, "merge keys (<<) aren't allowed: write the keys out"));
            return;
        }

        if (!keys.Add(key.Value!))
        {
            errors.Add(ReaderErrors.Create(ErrorCodes.PlanDuplicateKey, "SY-18", key.Start,
                $"the key '{key.Value}' appears more than once in this mapping"));
        }
    }

    private static BlokkyError Yaml(string? rule, PlanPosition at, string message) =>
        ReaderErrors.Create(ErrorCodes.PlanYaml, rule, at, message);

    /// <summary>An open collection; <see cref="Keys"/> is null for a sequence.</summary>
    private sealed class Frame(HashSet<string>? keys)
    {
        public HashSet<string>? Keys { get; } = keys;

        public bool ExpectKey { get; set; } = true;
    }
}
