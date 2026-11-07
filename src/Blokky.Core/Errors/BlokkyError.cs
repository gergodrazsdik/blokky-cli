using System.Text.Json.Nodes;

namespace Blokky.Core.Errors;

/// <summary>
/// The error object of output-contract.md §5. Property order is the JSON order; null members are omitted.
/// </summary>
public sealed record BlokkyError
{
    /// <summary>A built-in <c>BK_</c> code (<see cref="ErrorCodes"/>) or a user code.</summary>
    public required string Code { get; init; }

    /// <summary>Human-readable text. Not a contract.</summary>
    public required string Message { get; init; }

    /// <summary>The path id of the node the error belongs to.</summary>
    public string? Node { get; init; }

    /// <summary><c>line:column</c>, for plan errors.</summary>
    public string? Pos { get; init; }

    /// <summary>A corrected YAML snippet, for targeted plan errors.</summary>
    public string? Fix { get; init; }

    /// <summary>Machine-readable fields, listed per code in the error catalogues.</summary>
    public JsonObject? Detail { get; init; }

    /// <summary>Nested errors.</summary>
    public IReadOnlyList<BlokkyError>? Causes { get; init; }

    /// <summary>Extra context.</summary>
    public JsonObject? Context { get; init; }

    /// <summary>Formats a 1-based position as <c>line:column</c>.</summary>
    public static string FormatPos(int line, int column) =>
        string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{line}:{column}");
}
