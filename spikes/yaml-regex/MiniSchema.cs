// Spike stand-in for the schema layer: only the few field rules the syntax/yaml and
// regex cases need, to show the event tree carries the positions they report at.
// Not a design for the real validator.
using YamlDotNet.Core;

sealed class MiniSchema(PlanYaml y, LineMap map)
{
    static readonly HashSet<string> Booleans = ["ignore_errors", "retry", "ignore_case", "fail_fast"];
    static readonly HashSet<string> ProbeKeys = ["exec", "script", "shell", "output", "cwd", "env", "timeout"];

    PlanError E(string code, YNode n, string detail = "")
    {
        var (l, c) = map.At(n.Index);
        return new(code, l, c, detail);
    }

    public void Check(YNode n, string? key = null)
    {
        if (n.Kind == 'q')
        {
            if (n.Seq.Count == 0 && key != "mounts") y.Errors.Add(E("BK_PLAN_VALUE", n, "rule=RF-21"));
            if (key == "script") y.Errors.Add(E("BK_PLAN_VALUE", n, "rule=SY-89"));
            if (key == "exec" && n.Flow && n.Seq.Count == 1 && n.Seq[0].Kind == 's'
                && n.Seq[0].Style == ScalarStyle.Plain && n.Seq[0].Value.Contains(' '))
                y.Warnings.Add(E("W_SY89_EXEC_BRACKET", n));
            foreach (var item in n.Seq) Check(item);
            return;
        }
        if (n.Kind == 'm')
        {
            if (n.Map.Count == 0) y.Errors.Add(E("BK_PLAN_VALUE", n, "rule=SY-22"));
            foreach (var (k, v) in n.Map)
            {
                if (key == "probe" && k.Kind == 's' && !ProbeKeys.Contains(k.Value))
                    y.Errors.Add(E("BK_PLAN_UNKNOWN_KEY", k));
                Check(v, k.Kind == 's' ? k.Value : null);
            }
            CheckRegexes(n); // matches/not_matches only occur in conditions and inputs
            return;
        }
        if (n.Kind != 's' || key == null) return;
        if (Booleans.Contains(key) && n.Value is not ("true" or "false"))
            y.Errors.Add(E("BK_PLAN_VALUE", n, "rule=SY-53"));
        if (key == "max" && !System.Text.RegularExpressions.Regex.IsMatch(n.Value, "^(0|[1-9][0-9]{0,18})$"))
            y.Errors.Add(E("BK_PLAN_VALUE", n, "rule=SY-54"));
        if (key is "exec" or "script" && n.Style == ScalarStyle.Plain
            && y.InlineCommentLines.Contains(map.At(n.EndIndex).Line))
            y.Warnings.Add(E("W_SY89_COMMENT_AFTER_VALUE", n));
    }

    void CheckRegexes(YNode cond)
    {
        bool ci = cond.Map.Any(e => e.Key.Value == "ignore_case" && e.Value.Value == "true");
        foreach (var (k, v) in cond.Map)
        {
            if (k.Value is not ("matches" or "not_matches") || v.Kind != 's') continue;
            try { PortableRegex.Compile(v.Value, ci); }
            catch (RegexSubsetException ex) { y.Errors.Add(E("BK_PLAN_VALUE", v, "rule=RF-22 " + ex.Message)); }
        }
    }
}
