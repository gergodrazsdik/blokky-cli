using Blokky.Core.Errors;

namespace Blokky.Cli;

/// <summary>The commands this build knows.</summary>
internal enum CommandKind
{
    Help,
    Version,
    Validate,
    Run,
}

/// <summary>A parsed invocation.</summary>
/// <param name="Kind">The command.</param>
/// <param name="PlanPath">The plan file, <c>-</c> for stdin; null for <c>--help</c> and <c>--version</c>.</param>
/// <param name="Inputs"><c>--input</c> values in command-line order (not bound yet).</param>
internal sealed record ParsedCommand(CommandKind Kind, string? PlanPath, IReadOnlyList<KeyValuePair<string, string>> Inputs);

/// <summary>
/// A small hand-written parser for the fixed command set. It keeps the brief's rules: one spelling per
/// option, no <c>--opt=value</c> or <c>--opt:value</c>, no bundling, no response files.
/// It reports every argument error it finds (cli.md: within a step, every error is reported).
/// </summary>
internal static class CommandLine
{
    internal const string UsageText =
        """
        usage:
            blokky validate [--input NAME=VALUE]... <plan-file | ->
            blokky run [--input NAME=VALUE]... <plan-file | ->
            blokky --version
            blokky --help

        This build checks a plan's structure, but supports no language features yet:
        every valid plan is rejected with BK_PLAN_NOT_SUPPORTED (exit 3) and nothing runs.
        """;

    public static (ParsedCommand? Command, IReadOnlyList<BlokkyError> Errors) Parse(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);
        if (args.Count == 0)
        {
            return Fail(Usage("missing command: expected validate, run, --version or --help"));
        }

        switch (args[0])
        {
            case "--help" when args.Count == 1:
                return (new ParsedCommand(CommandKind.Help, null, []), []);
            case "--version" when args.Count == 1:
                return (new ParsedCommand(CommandKind.Version, null, []), []);
            case "--help" or "--version":
                return Fail(Usage($"{args[0]} takes no arguments"));
            case "validate":
                return ParsePlanCommand(CommandKind.Validate, args);
            case "run":
                return ParsePlanCommand(CommandKind.Run, args);
            default:
                return Fail(Usage($"unknown command '{args[0]}': expected validate, run, --version or --help"));
        }
    }

    private static (ParsedCommand?, IReadOnlyList<BlokkyError>) ParsePlanCommand(CommandKind kind, IReadOnlyList<string> args)
    {
        var errors = new List<BlokkyError>();
        var inputs = new List<KeyValuePair<string, string>>();
        var positionals = new List<string>();
        var command = args[0];
        var optionsEnded = false;

        for (var i = 1; i < args.Count; i++)
        {
            var arg = args[i];
            if (optionsEnded || arg == "-" || !arg.StartsWith('-'))
            {
                positionals.Add(arg);
            }
            else if (arg == "--")
            {
                optionsEnded = true;
            }
            else if (arg == "--input")
            {
                if (i + 1 >= args.Count)
                {
                    errors.Add(Usage("--input needs a value: --input NAME=VALUE"));
                    break;
                }

                i++;
                if (TryParseInput(args[i], out var input, out var error))
                {
                    inputs.Add(input);
                }
                else
                {
                    errors.Add(error);
                }
            }
            else if (arg.StartsWith("--input=", StringComparison.Ordinal) || arg.StartsWith("--input:", StringComparison.Ordinal))
            {
                errors.Add(Usage($"'{arg}': write the value as a separate argument: --input {arg[8..]}"));
            }
            else
            {
                errors.Add(Usage($"unknown option '{arg}' for '{command}'"));
            }
        }

        switch (positionals.Count)
        {
            case 0:
                errors.Add(Usage($"'{command}' needs a plan file, or - for stdin"));
                break;
            case > 1:
                errors.Add(Usage($"'{command}' takes one plan file; got {positionals.Count} arguments: {string.Join(' ', positionals)}"));
                break;
        }

        return errors.Count > 0 ? (null, errors) : (new ParsedCommand(kind, positionals[0], inputs), []);
    }

    /// <summary><c>NAME=VALUE</c>: a non-empty name of <c>[A-Za-z0-9_]</c> (BK_INPUT_SYNTAX, SY-52); the value may be empty.</summary>
    private static bool TryParseInput(string text, out KeyValuePair<string, string> input, out BlokkyError error)
    {
        var eq = text.IndexOf('=', StringComparison.Ordinal);
        var name = eq < 0 ? text : text[..eq];
        if (eq <= 0 || !name.All(c => char.IsAsciiLetterOrDigit(c) || c == '_'))
        {
            input = default;
            error = new BlokkyError
            {
                Code = ErrorCodes.InputSyntax,
                Message = eq < 0
                    ? $"--input '{text}' has no '=': write --input NAME=VALUE"
                    : $"--input '{text}': the name must be letters, digits and '_'",
            };
            return false;
        }

        input = new KeyValuePair<string, string>(name, text[(eq + 1)..]);
        error = null!;
        return true;
    }

    private static BlokkyError Usage(string message) => new() { Code = ErrorCodes.Usage, Message = message };

    private static (ParsedCommand?, IReadOnlyList<BlokkyError>) Fail(BlokkyError error) => (null, [error]);
}
