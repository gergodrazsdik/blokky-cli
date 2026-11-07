// Spikes 3 and 9 (throwaway). Usage:
//   plan <file>          plan errors and warnings: "E|W CODE line col detail"
//   regex-check <file>   lines: pattern \t ignore_case(0|1) \t value \t expect(1|0|invalid), \-escaped
//   regex-probe          .NET engine behaviour the translation works around
using System.Text;
using System.Text.RegularExpressions;

switch (args.FirstOrDefault())
{
    case "plan": return Plan(File.ReadAllBytes(args[1]));
    case "regex-check": return RegexCheck(File.ReadAllLines(args[1]), args.Contains("--naive"));
    case "regex-probe": RegexProbe(); return 0;
    default: Console.Error.WriteLine("usage: plan <file> | regex-check <file> | regex-probe"); return 2;
}

static int Plan(byte[] raw)
{
    var errors = PlanText.Decode(raw, out string text);
    var warnings = new List<PlanError>();
    if (errors.Count == 0)
    {
        var map = new LineMap(text);
        errors = PlanText.Lint(text, map);
        if (errors.Count == 0)
        {
            var y = new PlanYaml(text, map);
            y.Parse();
            if (y.Root != null) new MiniSchema(y, map).Check(y.Root);
            errors = y.Errors;
            warnings = y.Warnings;
        }
    }
    foreach (var e in errors) Console.WriteLine($"E {e.Code} {e.Line} {e.Col} {e.Detail}");
    foreach (var w in warnings) Console.WriteLine($"W {w.Code} {w.Line} {w.Col} {w.Detail}");
    return errors.Count == 0 ? 0 : 1;
}

static string Unescape(string s)
{
    var sb = new StringBuilder();
    for (int i = 0; i < s.Length; i++)
    {
        if (s[i] != '\\') { sb.Append(s[i]); continue; }
        char c = s[++i];
        if (c == 'u') // \u{HEX}
        {
            int end = s.IndexOf('}', i);
            sb.Append(char.ConvertFromUtf32(Convert.ToInt32(s[(i + 2)..end], 16)));
            i = end;
        }
        else sb.Append(c switch { 't' => '\t', 'n' => '\n', 'r' => '\r', _ => c });
    }
    return sb.ToString();
}

static int RegexCheck(string[] lines, bool naive)
{
    int pass = 0, fail = 0;
    foreach (var line in lines)
    {
        if (line.Length == 0 || line.StartsWith('#')) continue;
        var f = line.Split('\t');
        string pat = Unescape(f[0]), val = Unescape(f[2]), want = f[3], label = f.Length > 4 ? f[4] : "";
        string got;
        try
        {
            got = naive
                ? (new Regex(pat, RegexOptions.NonBacktracking | RegexOptions.CultureInvariant | (f[1] == "1" ? RegexOptions.IgnoreCase : 0)).IsMatch(val) ? "1" : "0")
                : PortableRegex.Compile(pat, f[1] == "1").IsMatch(val) ? "1" : "0";
        }
        catch (ArgumentException) { got = "invalid"; }
        catch (RegexSubsetException ex) { got = "invalid"; if (want != "invalid") label += " [" + ex.Message + "]"; }
        if (got == want) pass++;
        else { fail++; Console.WriteLine($"FAIL {label}: /{f[0]}/ ci={f[1]} on '{f[2]}' want {want} got {got}"); }
    }
    Console.WriteLine($"regex checks: {pass} pass, {fail} fail");
    return fail == 0 ? 0 : 1;
}

static void RegexProbe()
{
    const RegexOptions NB = RegexOptions.NonBacktracking | RegexOptions.CultureInvariant;
    static void T(string label, string pat, string input, RegexOptions o)
    {
        string r;
        try { r = new Regex(pat, o).IsMatch(input).ToString(); }
        catch (Exception e) { r = e.GetType().Name + ": " + e.Message.Split('\n')[0]; }
        Console.WriteLine($"{label,-44} {r}");
    }
    T("IgnoreCase: Kelvin sign vs ^k$ (CO-12: no)", "^k$", "\u212A", NB | RegexOptions.IgnoreCase);
    T("IgnoreCase: long s vs ^s$ (CO-12: yes)", "^s$", "ſ", NB | RegexOptions.IgnoreCase);
    T("IgnoreCase: dotless i vs ^I$ (CO-12: yes)", "^I$", "ı", NB | RegexOptions.IgnoreCase);
    T("IgnoreCase: final sigma vs ^σ$ (CO-12: yes)", "^σ$", "ς", NB | RegexOptions.IgnoreCase);
    T("IgnoreCase: sharp s vs ^SS$ (CO-12: no)", "^SS$", "ß", NB | RegexOptions.IgnoreCase);
    T("NB lookahead a(?=b)", "a(?=b)", "ab", NB);
    T("NB \\b in über (ASCII: boundary)", "\\bber", "über", NB);
    T("NB + ECMAScript", "\\bber", "über", NB | RegexOptions.ECMAScript);
    T("NB lazy a{1,2}?b on aaab", "a{1,2}?b", "aaab", NB);
    T("NB abc$ on abc\\n (spec: no)", "abc$", "abc\n", NB);
    T("NB ^.$ on U+1F600 (spec: yes)", "^.$", "\U0001F600", NB);
    T("NB \\d on U+0663 (spec: no)", "\\d", "٣", NB);
    T("NB (?:a{100}){100}", "(?:a{100}){100}", "a", NB);
    Console.WriteLine($"OrdinalIgnoreCase long s = s (CO-12: yes)     {string.Equals("ſ", "s", StringComparison.OrdinalIgnoreCase)}");
    Console.WriteLine($"OrdinalIgnoreCase dotless i = I (CO-12: yes)  {string.Equals("ı", "I", StringComparison.OrdinalIgnoreCase)}");
    Console.WriteLine($"char.ToUpperInvariant(U+017F) = U+{(int)char.ToUpperInvariant('ſ'):X4} (Unicode: U+0053)");
    string value = string.Concat(Enumerable.Repeat("aé\U0001F600 ", 1500));
    foreach (var limit in new[] { 0, 2_000_000 })
    {
        if (limit > 0) AppContext.SetData("REGEX_NONBACKTRACKING_MAX_AUTOMATA_SIZE", limit);
        Console.WriteLine(limit > 0 ? $"-- automata limit raised to {limit}" : "-- default automata limit");
        foreach (var pat in new[] { ".{1000}", "[^a]{300}", "(?i)[a-z]{500}", "\\w{1000}", "(?:.{1000}){1000}" })
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                var r = PortableRegex.Compile(pat);
                long compile = sw.ElapsedMilliseconds;
                r.IsMatch(value);
                Console.WriteLine($"translated {pat,-33} ok, compile {compile} ms, match over {value.Length} chars {sw.ElapsedMilliseconds - compile} ms");
            }
            catch (RegexSubsetException e) { Console.WriteLine($"translated {pat,-33} {e.Message[..Math.Min(110, e.Message.Length)]}"); }
        }
    }
}
