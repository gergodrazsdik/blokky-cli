using Blokky.Core.Ast;

namespace Blokky.Core.Validation.Rules.Reference;

/// <summary>
/// SH-17 (shells.md §2.2): when <c>shell:</c> or <c>defaults.shell</c> names <c>pwsh</c> or <c>powershell</c>, a script
/// whose prologue line (the first that isn't blank, a comment or a <c>using</c> statement) starts a <c>param</c> block
/// (after optional attributes) or a top-level named block (<c>begin</c>, <c>process</c>, <c>end</c>, <c>clean</c>,
/// <c>dynamicparam</c>) is a plan error: plan scripts receive no arguments or pipeline input, and the prologue would have no
/// valid place. Checked for script leaves, services and probes; a shell found by detection or configuration isn't checked.
/// The error is at the script's first content line (SY-1).
/// </summary>
public sealed class PwshScriptRule : IPlanRule
{
    private static readonly string[] NamedBlocks = ["begin", "process", "end", "clean", "dynamicparam"];

    /// <inheritdoc/>
    public IReadOnlyList<string> Rules { get; } = ["SH-17"];

    /// <inheritdoc/>
    public void Check(PlanContext context, PlanErrorSink errors)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(errors);
        var defaults = context.Ast.Defaults?.Shell?.Value;
        foreach (var node in PlanWalk.Nodes(context.Ast))
        {
            if (node.Kind == NodeKind.Script)
            {
                Script(node.Script, Named(node.Has("shell"), node.Shell, defaults), node.Path, errors);
            }

            foreach (var service in node.Services ?? [])
            {
                Script(service.Script, Named(service.Has("shell"), service.Shell, defaults), node.Path, errors);
            }

            foreach (var (_, condition) in PlanWalk.Conditions(node))
            {
                foreach (var c in PlanWalk.Within(condition))
                {
                    foreach (var subject in c.Subjects)
                    {
                        if (subject.Probe is { } probe)
                        {
                            Script(probe.Script, Named(probe.Has("shell"), probe.Shell, defaults), node.Path, errors);
                        }
                    }
                }
            }
        }
    }

    /// <summary>Whether <paramref name="text"/> starts with a <c>param</c> block or a named block at its prologue line.</summary>
    internal static bool Rejects(string text)
    {
        var i = SkipPreamble(text);
        var attributes = false;
        while (i < text.Length && text[i] == '[')
        {
            var close = Close(text, i);
            if (close < 0)
            {
                return false;
            }

            attributes = true;
            i = SkipTrivia(text, close + 1);
        }

        var word = Word(text, i);
        if (word.Equals("param", StringComparison.OrdinalIgnoreCase))
        {
            var next = SkipTrivia(text, i + word.Length);
            return next < text.Length && text[next] == '(';
        }

        if (!attributes && NamedBlocks.Any(b => word.Equals(b, StringComparison.OrdinalIgnoreCase)))
        {
            var next = SkipTrivia(text, i + word.Length);
            return next < text.Length && text[next] == '{';
        }

        return false;
    }

    private static ShellName? Named(bool hasKey, Typed<ShellName>? own, ShellName? defaults) =>
        hasKey ? own?.Value : defaults;

    private static void Script(PlanScalar? script, ShellName? shell, string node, PlanErrorSink errors)
    {
        if (script is not null && shell is ShellName.Pwsh or ShellName.Powershell && Rejects(script.Text))
        {
            errors.Invalid("SH-17", script.Position, node,
                $"a {(shell == ShellName.Pwsh ? "pwsh" : "powershell")} plan script can't start with a param block or a named "
                + "block: plan scripts receive no arguments or pipeline input",
                "remove the param block (pass values with env: or export:) and the block names");
        }
    }

    /// <summary>Skips blank lines, comments and <c>using</c> statements: the index of the prologue line's first code.</summary>
    private static int SkipPreamble(string text)
    {
        var i = 0;
        while (true)
        {
            i = SkipTrivia(text, i);
            var word = Word(text, i);
            if (!word.Equals("using", StringComparison.OrdinalIgnoreCase)
                || i + word.Length >= text.Length || !char.IsWhiteSpace(text[i + word.Length]))
            {
                return i;
            }

            var end = text.IndexOf('\n', i);
            i = end < 0 ? text.Length : end + 1;
        }
    }

    /// <summary>Skips whitespace, line breaks, <c># …</c> line comments and <c>&lt;# … #&gt;</c> block comments.</summary>
    private static int SkipTrivia(string text, int i)
    {
        while (i < text.Length)
        {
            if (char.IsWhiteSpace(text[i]))
            {
                i++;
            }
            else if (text[i] == '<' && i + 1 < text.Length && text[i + 1] == '#')
            {
                var end = text.IndexOf("#>", i + 2, StringComparison.Ordinal);
                if (end < 0)
                {
                    return text.Length;
                }

                i = end + 2;
            }
            else if (text[i] == '#')
            {
                var end = text.IndexOf('\n', i);
                i = end < 0 ? text.Length : end + 1;
            }
            else
            {
                break;
            }
        }

        return i;
    }

    /// <summary>The bare word at <paramref name="i"/> (letters, digits, <c>_</c> and <c>-</c>), or empty.</summary>
    private static string Word(string text, int i)
    {
        var end = i;
        while (end < text.Length && (char.IsLetterOrDigit(text[end]) || text[end] is '_' or '-'))
        {
            end++;
        }

        return text[i..end];
    }

    /// <summary>The index of the <c>]</c> that closes the attribute at <paramref name="open"/>, or -1.</summary>
    private static int Close(string text, int open)
    {
        var depth = 0;
        for (var i = open; i < text.Length; i++)
        {
            if (text[i] == '[')
            {
                depth++;
            }
            else if (text[i] == ']' && --depth == 0)
            {
                return i;
            }
        }

        return -1;
    }
}
