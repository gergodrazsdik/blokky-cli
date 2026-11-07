using System.Text.Json;

namespace Blokky.Core.Contracts;

/// <summary>Renders contract documents as the bytes stdout carries: UTF-8, no BOM, one trailing LF (OC-1).</summary>
public static class ContractWriter
{
    /// <summary>Serializes <paramref name="envelope"/> as one compact JSON document followed by LF.</summary>
    public static byte[] ToUtf8(PreRunEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        var json = JsonSerializer.SerializeToUtf8Bytes(envelope, ContractJsonContext.Contract.PreRunEnvelope);
        var bytes = new byte[json.Length + 1];
        json.CopyTo(bytes, 0);
        bytes[^1] = (byte)'\n';
        return bytes;
    }
}
