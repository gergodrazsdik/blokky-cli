// Spike 3: YAML subset (SY-14..SY-22) on YamlDotNet's scanner and event parser only.
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using Tok = YamlDotNet.Core.Tokens;

sealed class YNode
{
    public char Kind;            // 's' scalar, 'm' mapping, 'q' sequence, 'a' alias
    public int Index, EndIndex;  // UTF-16 indexes into the BOM-less text (position fixed for block scalars)
    public string Value = "";
    public ScalarStyle Style;
    public bool Flow;
    public readonly List<(YNode Key, YNode Value)> Map = [];
    public readonly List<YNode> Seq = [];
}

sealed class PlanYaml(string text, LineMap map)
{
    public const int MaxDepth = 128;
    public readonly List<PlanError> Errors = [];
    public readonly List<PlanError> Warnings = [];
    public readonly HashSet<int> InlineCommentLines = [];
    public YNode? Root;

    PlanError E(string code, int index, string detail = "")
    {
        var (l, c) = map.At(index);
        return new(code, l, c, detail);
    }

    static int Ix(Mark m) => (int)m.Index;

    public void Parse()
    {
        TokenPass();
        var events = new List<ParsingEvent>();
        bool failed = false;
        try
        {
            var parser = new Parser(new StringReader(text));
            int depth = 0, docs = 0;
            while (parser.MoveNext())
            {
                var ev = parser.Current!;
                events.Add(ev);
                switch (ev)
                {
                    case DocumentStart ds:
                        docs++;
                        if (docs > 1) Errors.Add(E("BK_PLAN_YAML", Ix(ds.Start), "SY-16 second document"));
                        break;
                    case AnchorAlias al:
                        Errors.Add(E("BK_PLAN_YAML", Ix(al.Start), "SY-14 alias"));
                        break;
                    case NodeEvent ne:
                        if (!ne.Anchor.IsEmpty) Errors.Add(E("BK_PLAN_YAML", Ix(ne.Start), "SY-14 anchor"));
                        if (!ne.Tag.IsEmpty) Errors.Add(E("BK_PLAN_YAML", Ix(ne.Start), "SY-15 tag"));
                        if (ne is MappingStart or SequenceStart && ++depth > MaxDepth)
                        {
                            // SY-9: caps end processing; reported at the first node that is too deep.
                            Errors.Clear();
                            Errors.Add(E("BK_PLAN_TOO_LARGE", Ix(ne.Start), "SY-9 depth"));
                            Root = null;
                            return;
                        }
                        break;
                    case MappingEnd or SequenceEnd:
                        depth--;
                        break;
                }
            }
        }
        catch (Exception ex0) when (ex0 is not OutOfMemoryException)
        {
            // YamlDotNet 18.1 also throws InvalidOperationException and ArgumentException for malformed input
            // (`exec: [a, b` then a block key; `!!st[r`); they carry no mark: use the last event.
            var ex = ex0 as YamlException ?? new YamlException(events.Count > 0 ? events[^1].End : Mark.Empty, events.Count > 0 ? events[^1].End : Mark.Empty, "malformed YAML (" + ex0.GetType().Name + ")");
            // SY-1/SY-89: a quoted scalar followed by more text on its line (`exec: "a" b`)
            // is reported at the opening quote, not where YamlDotNet gave up.
            int at = Ix(ex.Start);
            if (events.Count > 0 && events[^1] is Scalar { Style: ScalarStyle.DoubleQuoted or ScalarStyle.SingleQuoted } q
                && map.At(Ix(q.Start)).Line == map.At(at).Line)
                at = Ix(q.Start);
            Errors.Add(E("BK_PLAN_YAML", at, "syntax: " + ex.Message.Split('\n')[0]));
            failed = true;
        }
        if (failed || Errors.Count > 0 && events.Count == 0) return;
        int pos = 0;
        while (pos < events.Count && events[pos] is not DocumentStart) pos++;
        if (pos >= events.Count) return;
        pos++;
        if (events[pos] is DocumentEnd) return; // empty document
        Root = Build(events, ref pos);
    }

    /// Scanner tokens: directives, explicit `?` keys, inline comments (SY-89).
    void TokenPass()
    {
        try
        {
            var sc = new Scanner(new StringReader(text), skipComments: false);
            Tok.Token? prev = null;
            while (sc.MoveNext())
            {
                // `: a` (an empty key): YamlDotNet 18.1 gives it a wrong mark and, in a
                // nested block mapping, drops its value event. SY-17 rejects it anyway,
                // so report it here at the ':' and let Build skip it.
                if (sc.Current is Tok.Value && prev is not (Tok.Scalar or Tok.FlowSequenceEnd or Tok.FlowMappingEnd or Tok.Anchor or Tok.Tag or Tok.AnchorAlias or null))
                    Errors.Add(E("BK_PLAN_YAML", Ix(sc.Current.Start), "SY-17 empty key"));
                if (sc.Current is not Tok.Comment) prev = sc.Current;
                switch (sc.Current)
                {
                    // YamlDotNet 18.1 can keep yielding Error tokens forever (`log: a: b`):
                    // stop at the first one; the event pass reports the syntax error.
                    case Tok.Error:
                        return;
                    case Tok.VersionDirective or Tok.TagDirective:
                        Errors.Add(E("BK_PLAN_YAML", Ix(sc.Current.Start), "SY-15 directive"));
                        break;
                    case Tok.Key k when Ix(k.Start) < text.Length && text[Ix(k.Start)] == '?':
                        Errors.Add(E("BK_PLAN_YAML", Ix(k.Start), "SY-17 complex key"));
                        break;
                    case Tok.Comment c when c.IsInline:
                        InlineCommentLines.Add(map.At(Ix(c.Start)).Line);
                        break;
                }
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException) { /* reported by the event pass */ }
    }

    YNode Build(List<ParsingEvent> ev, ref int pos)
    {
        var e = ev[pos++];
        switch (e)
        {
            case Scalar s:
                return new YNode
                {
                    Kind = 's', Value = s.Value, Style = s.Style,
                    Index = s.Style is ScalarStyle.Literal or ScalarStyle.Folded ? BlockContentStart(Ix(s.Start)) : Ix(s.Start),
                    EndIndex = Ix(s.End),
                };
            case AnchorAlias a:
                return new YNode { Kind = 'a', Index = Ix(a.Start), EndIndex = Ix(a.End) };
            case SequenceStart ss:
            {
                var n = new YNode { Kind = 'q', Index = Ix(ss.Start), Flow = ss.Style == SequenceStyle.Flow };
                while (ev[pos] is not SequenceEnd) n.Seq.Add(Build(ev, ref pos));
                n.EndIndex = Ix(ev[pos++].End);
                return n;
            }
            case MappingStart ms:
            {
                var n = new YNode { Kind = 'm', Index = Ix(ms.Start), Flow = ms.Style == MappingStyle.Flow };
                var seen = new HashSet<string>(StringComparer.Ordinal);
                while (ev[pos] is not MappingEnd)
                {
                    var k = Build(ev, ref pos);
                    if (ev[pos] is MappingEnd) break; // value event missing (see TokenPass)
                    var v = Build(ev, ref pos);
                    if (k.Kind == 's' && k.Style == ScalarStyle.Plain && k.Value.Length == 0) continue; // empty key: reported
                    if (k.Kind != 's')
                    {
                        if (k.Kind != 'a') Errors.Add(E("BK_PLAN_YAML", k.Index, "SY-17 non-scalar key"));
                    }
                    else if (k.Style == ScalarStyle.Plain && k.Value == "<<")
                        Errors.Add(E("BK_PLAN_YAML", k.Index, "SY-14 merge key"));
                    else if (!seen.Add(k.Value))
                        Errors.Add(E("BK_PLAN_DUPLICATE_KEY", k.Index, "SY-18"));
                    // SY-20, except a flow-mapping entry with no ':' at all (`{ exec: a,b }`):
                    // that is SY-89's comma pitfall, whose targeted error replaces SY-20 (SY-3).
                    if (v.Kind == 's' && v.Style == ScalarStyle.Plain && v.Value.Length == 0
                        && !(n.Flow && !HasColonAfter(k.EndIndex)))
                        Errors.Add(E("BK_PLAN_VALUE", k.Index, "rule=SY-20"));
                    n.Map.Add((k, v));
                }
                n.EndIndex = Ix(ev[pos++].End);
                return n;
            }
            default:
                throw new InvalidOperationException($"unexpected event {e}");
        }
    }

    bool HasColonAfter(int i)
    {
        while (i < text.Length && text[i] == ' ') i++;
        return i < text.Length && text[i] == ':';
    }

    /// SY-1: a block scalar is located at the first character of its first content line.
    int BlockContentStart(int indicator)
    {
        int i = indicator;
        while (i < text.Length && text[i] != '\n' && text[i] != '\r') i++;
        while (i < text.Length)
        {
            if (text[i] == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
            i++; // past the line break
            int j = i;
            while (j < text.Length && text[j] == ' ') j++;
            if (j < text.Length && text[j] != '\n' && text[j] != '\r') return j;
            i = j;
        }
        return indicator;
    }
}
