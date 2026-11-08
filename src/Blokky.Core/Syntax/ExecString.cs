using System.Text;
using Blokky.Core.Ast;

namespace Blokky.Core.Syntax;

/// <summary>
/// The <c>exec</c> string form (shells.md §1.2): split into argv by SH-2 to SH-6, with no expansion, globbing or operator
/// (SH-7). The argv is static (SH-8), so the validator, <c>leaves</c> and the executor all split the same way.
/// </summary>
public static class ExecString
{
    private const string Fix = "exec: [prog, arg, { var: X }]";

    /// <summary>
    /// Splits <paramref name="text"/> into <paramref name="argv"/>; null when it splits, otherwise SH-7 with the first
    /// problem. A <c>${…}</c> (outside single quotes) is left to RF-9, which reports interpolation in a literal field: it is
    /// kept as written and not reported here.
    /// </summary>
    public static GrammarError? Split(string text, out IReadOnlyList<string> argv)
    {
        ArgumentNullException.ThrowIfNull(text);
        var words = new List<string>();
        argv = words;
        var word = new StringBuilder();
        var inWord = false;
        var i = 0;
        while (i < text.Length)
        {
            var c = text[i];
            switch (c)
            {
                case ' ' or '\t' or '\n':
                    if (inWord)
                    {
                        words.Add(word.ToString());
                        word.Clear();
                        inWord = false;
                    }

                    i++;
                    break;
                case '\'':
                    var close = text.IndexOf('\'', i + 1);
                    if (close < 0)
                    {
                        return Error("a single quote is never closed");
                    }

                    word.Append(text, i + 1, close - i - 1);
                    inWord = true;
                    i = close + 1;
                    break;
                case '"':
                    if (DoubleQuoted(text, ref i, word) is { } quoted)
                    {
                        return quoted;
                    }

                    inWord = true;
                    break;
                case '\\':
                    if (i + 1 == text.Length)
                    {
                        return Error("a trailing '\\' escapes nothing");
                    }

                    word.Append(text[i + 1]);
                    inWord = true;
                    i += 2;
                    break;
                case '$' when i + 1 < text.Length && text[i + 1] == '{':
                    SkipInterpolation(text, ref i, word);
                    inWord = true;
                    break;
                case '$':
                    return Error("'$' outside single quotes: the string form expands no variables; pass { var: X } in the array form");
                case '`':
                    return Error("a backtick outside single quotes: the string form runs no command substitution");
                case '|' or '&' or ';' or '<' or '>' or '(' or ')':
                    return Error($"an unquoted '{c}': the string form runs no shell, so it has no pipes, redirections or "
                        + "operators; use a script");
                case '*' or '?' or '[' or ']' or '{' or '}':
                    return Error($"an unquoted '{c}': the string form does no globbing or brace expansion; quote it, or use "
                        + "for: { glob } or a script");
                case '~' or '#' when !inWord:
                    return Error(c == '~'
                        ? "an unquoted '~' at the start of a word: the string form doesn't expand ~; write the path, or quote it"
                        : "an unquoted '#' at the start of a word: the string form has no comments; quote it");
                default:
                    word.Append(c);
                    inWord = true;
                    i++;
                    break;
            }
        }

        if (inWord)
        {
            words.Add(word.ToString());
        }

        return words.Count == 0 ? Error("it has no words: give the program to run") : null;
    }

    private static GrammarError? DoubleQuoted(string text, ref int i, StringBuilder word)
    {
        i++;
        while (i < text.Length)
        {
            var c = text[i];
            switch (c)
            {
                case '"':
                    i++;
                    return null;
                case '\\' when i + 1 < text.Length && text[i + 1] is '"' or '\\':
                    word.Append(text[i + 1]);
                    i += 2;
                    break;
                case '$' when i + 1 < text.Length && text[i + 1] == '{':
                    SkipInterpolation(text, ref i, word);
                    break;
                case '$':
                    return Error("'$' inside double quotes: the string form expands no variables; use single quotes, "
                        + "or { var: X } in the array form");
                case '`':
                    return Error("a backtick inside double quotes: the string form runs no command substitution; use single quotes");
                default:
                    word.Append(c);
                    i++;
                    break;
            }
        }

        return Error("a double quote is never closed");
    }

    /// <summary>A <c>${…}</c>, RF-9's mistake: kept as written, up to its '}' (or the end).</summary>
    private static void SkipInterpolation(string text, ref int i, StringBuilder word)
    {
        var close = text.IndexOf('}', i + 2);
        var end = close < 0 ? text.Length : close + 1;
        word.Append(text, i, end - i);
        i = end;
    }

    private static GrammarError Error(string why) =>
        new("SH-7", $"exec: the command string can't be split: {why}", Fix);

    /// <summary>
    /// SH-10: a literal argv[0] ending in <c>.bat</c> or <c>.cmd</c>, in any case, which cmd.exe would re-parse. Null if
    /// <paramref name="program"/> is fine.
    /// </summary>
    public static GrammarError? BatchFile(string program)
    {
        ArgumentNullException.ThrowIfNull(program);
        return program.EndsWith(".bat", StringComparison.OrdinalIgnoreCase) || program.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase)
            ? new("SH-10", $"exec: '{program}' is a batch file: cmd.exe would re-parse its arguments with its own rules; "
                + "run such tools (npm, npx) in a PowerShell script", "script: npm ci\nshell: pwsh")
            : null;
    }
}
