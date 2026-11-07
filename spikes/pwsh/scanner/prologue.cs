// Spike 14: SH-17 prologue insertion as a line scanner (throwaway).
// Usage: prologue <in.ps1> <out.ps1> [--windows]
// Exit 0 = written; 2 = SH-17 plan error (param( or attribute + param( at the insertion point).
using System.Text;
using System.Text.RegularExpressions;

var src = File.ReadAllText(args[0]);
var windows = args.Contains("--windows");
var prologue = windows
    ? "$ErrorActionPreference = 'Stop'; [Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false); "
    : "$ErrorActionPreference = 'Stop'; ";

var r = Scanner.Insert(src, prologue);
if (r.Error is not null)
{
    Console.Error.WriteLine($"SH-17: {r.Error} (line {r.Line})");
    return 2;
}
File.WriteAllText(args[1], r.Text!, new UTF8Encoding(false));
Console.WriteLine($"prologue at line {r.Line}");
return 0;

static partial class Scanner
{
    // Lines are split on LF only; a trailing CR stays with its line (YAML gives LF, SH-12).
    public static (string? Text, string? Error, int Line) Insert(string src, string prologue)
    {
        var lines = src.Split('\n');
        var inBlock = false;
        for (var i = 0; i < lines.Length; i++)
        {
            var t = lines[i].TrimStart(' ', '\t');
            if (inBlock)
            {
                var end = t.IndexOf("#>", StringComparison.Ordinal);
                if (end < 0) continue;
                inBlock = false;
                // Code after "#>" on the closing line: the spec text says "start of the line",
                // which would land inside the comment. Rule used here: the line counts as a comment line
                // only if the rest is blank or a line comment; otherwise it is reported.
                var rest = t[(end + 2)..].TrimStart(' ', '\t');
                if (rest.Length == 0 || rest.StartsWith('#')) continue;
                return (null, "code after the end of a block comment at the insertion point", i + 1);
            }
            if (t.TrimEnd('\r').Length == 0) continue;               // blank
            if (t.StartsWith("<#", StringComparison.Ordinal))         // block comment start
            {
                var end = t.IndexOf("#>", 2, StringComparison.Ordinal);
                if (end < 0) { inBlock = true; continue; }
                var rest = t[(end + 2)..].TrimStart(' ', '\t').TrimEnd('\r');
                if (rest.Length == 0 || rest.StartsWith('#')) continue;
                return (null, "code after a one-line block comment at the insertion point", i + 1);
            }
            if (t.StartsWith('#')) continue;                          // line comment, #requires
            if (UsingRx().IsMatch(t)) continue;                       // using namespace|module|assembly
            if (ParamAhead(lines, i)) return (null, "param( block", i + 1);
            if (NamedBlockRx().IsMatch(t)) return (null, "named script block (begin/process/end/clean/dynamicparam)", i + 1);
            var indent = lines[i][..(lines[i].Length - t.Length)];
            lines[i] = indent + prologue + t;
            return (string.Join('\n', lines), null, i + 1);
        }
        return (src, null, 0); // no statement: nothing to stop, no prologue
    }

    // param( at this line, or one or more attributes [...] (any lines, comments between) then param(.
    static bool ParamAhead(string[] lines, int i)
    {
        var rest = string.Join('\n', lines[i..]);
        var p = 0;
        while (true)
        {
            p = SkipTrivia(rest, p);
            if (p >= rest.Length) return false;
            if (rest[p] == '[')
            {
                var depth = 0;
                for (; p < rest.Length; p++)
                {
                    if (rest[p] == '[') depth++;
                    else if (rest[p] == ']' && --depth == 0) { p++; break; }
                }
                // a type literal followed by '::', '.', '$', an operator etc. is an expression, not an attribute
                var q = SkipTrivia(rest, p);
                if (q < rest.Length && rest[q] != '[' && !ParamRx().IsMatch(rest[q..])) return false;
                continue;
            }
            return ParamRx().IsMatch(rest[p..]);
        }
    }

    static int SkipTrivia(string s, int p)
    {
        while (p < s.Length)
        {
            if (char.IsWhiteSpace(s[p])) { p++; continue; }
            if (s[p] == '<' && p + 1 < s.Length && s[p + 1] == '#')
            {
                var e = s.IndexOf("#>", p + 2, StringComparison.Ordinal);
                p = e < 0 ? s.Length : e + 2; continue;
            }
            if (s[p] == '#') { var e = s.IndexOf('\n', p); p = e < 0 ? s.Length : e + 1; continue; }
            break;
        }
        return p;
    }

    [GeneratedRegex(@"^using\s+(namespace|module|assembly)\b", RegexOptions.IgnoreCase)]
    private static partial Regex UsingRx();
    [GeneratedRegex(@"^param\s*\(", RegexOptions.IgnoreCase)]
    private static partial Regex ParamRx();
    [GeneratedRegex(@"^(begin|process|end|clean|dynamicparam)\s*\{", RegexOptions.IgnoreCase)]
    private static partial Regex NamedBlockRx();
}
