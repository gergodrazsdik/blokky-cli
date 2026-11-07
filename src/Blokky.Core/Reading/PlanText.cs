using System.Buffers;
using System.Text.Json.Nodes;
using System.Text.Unicode;
using Blokky.Core.Errors;

namespace Blokky.Core.Reading;

/// <summary>
/// The encoding pass (syntax.md §2.2, §2.3): the size cap on the raw bytes, byte-order marks and UTF-8 validity. Each
/// failure is the only error reported for the text.
/// </summary>
public static class PlanText
{
    /// <summary>
    /// The plan size cap, 4 MiB counted on the raw bytes (SY-9). Hosts read at most one byte more, so the error doesn't
    /// state the plan's size.
    /// </summary>
    public const int MaxBytes = 4 * 1024 * 1024;

    private static ReadOnlySpan<byte> Utf8Bom => [0xEF, 0xBB, 0xBF];

    /// <summary>
    /// Decodes <paramref name="raw"/>: the text without its UTF-8 BOM, which is ignored (SY-5, defect 5 of F-0025: the
    /// YAML front end never sees it), or the one error that ends processing.
    /// </summary>
    public static (string? Text, BlokkyError? Error) Decode(ReadOnlySpan<byte> raw)
    {
        if (raw.Length > MaxBytes)
        {
            return (null, ReaderErrors.Create(ErrorCodes.PlanTooLarge, "SY-9", PlanPosition.Start,
                $"the plan is larger than the 4 MiB cap ({MaxBytes} bytes)"));
        }

        if (WideBom(raw) is { } encoding)
        {
            return (null, ReaderErrors.Create(ErrorCodes.PlanEncoding, "SY-6", PlanPosition.Start,
                $"the plan is {encoding} (it starts with a {encoding} byte-order mark), but plans must be UTF-8; this usually comes "
                + "from PowerShell's Out-File or > redirection: write it with -Encoding utf8, or pass the plan as a file"));
        }

        var bom = raw.StartsWith(Utf8Bom) ? Utf8Bom.Length : 0;
        var body = raw[bom..];
        var buffer = ArrayPool<char>.Shared.Rent(Math.Max(body.Length, 1));
        try
        {
            var status = Utf8.ToUtf16(body, buffer, out var bytesRead, out var charsWritten, replaceInvalidSequences: false);
            if (status == OperationStatus.Done)
            {
                return (new string(buffer, 0, charsWritten), null);
            }

            // SY-7: the first invalid byte; its offset counts from the first raw byte, a BOM included.
            var prefix = new string(buffer, 0, charsWritten);
            var position = new LineMap(prefix).At(prefix.Length);
            var offset = bom + bytesRead;
            return (null, ReaderErrors.Create(ErrorCodes.PlanEncoding, "SY-7", position,
                $"the plan isn't valid UTF-8: invalid byte 0x{body[bytesRead]:X2} at byte offset {offset}", "offset",
                JsonValue.Create(offset)));
        }
        finally
        {
            ArrayPool<char>.Shared.Return(buffer);
        }
    }

    /// <summary>A UTF-16 or UTF-32 byte-order mark (SY-6); UTF-32 first, since <c>FF FE 00 00</c> starts with <c>FF FE</c>.</summary>
    private static string? WideBom(ReadOnlySpan<byte> raw) => raw switch
    {
        [0xFF, 0xFE, 0x00, 0x00, ..] => "UTF-32LE",
        [0x00, 0x00, 0xFE, 0xFF, ..] => "UTF-32BE",
        [0xFF, 0xFE, ..] => "UTF-16LE",
        [0xFE, 0xFF, ..] => "UTF-16BE",
        _ => null,
    };
}
