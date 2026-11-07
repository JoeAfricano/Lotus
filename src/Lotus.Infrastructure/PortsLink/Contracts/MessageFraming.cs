using System.Buffers.Binary;
using System.Text.Json;

namespace Lotus.Integration.Contracts;

public sealed class MessageFramingException(string message) : Exception(message);

/// <summary>Each message is a 4-byte little-endian length followed by that many bytes of UTF-8 JSON.</summary>
public static class MessageFraming
{
    public static async Task WriteAsync(Stream stream, JsonRpcMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        var body = JsonSerializer.SerializeToUtf8Bytes(message, ContractJson.Options);
        if (body.Length > Protocol.MaxMessageBytes) throw new MessageFramingException($"Message of {body.Length} bytes exceeds the limit.");
        var header = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(header, body.Length);
        await stream.WriteAsync(header, cancellationToken).ConfigureAwait(false);
        await stream.WriteAsync(body, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Returns null when the stream ended cleanly before a new message.</summary>
    public static async Task<JsonRpcMessage?> ReadAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        var header = new byte[4];
        var read = await ReadFullyAsync(stream, header, cancellationToken).ConfigureAwait(false);
        if (read == 0) return null;
        if (read < header.Length) throw new MessageFramingException("Stream ended inside a message header.");
        var length = BinaryPrimitives.ReadInt32LittleEndian(header);
        if (length <= 0 || length > Protocol.MaxMessageBytes) throw new MessageFramingException($"Invalid message length {length}.");
        var body = new byte[length];
        if (await ReadFullyAsync(stream, body, cancellationToken).ConfigureAwait(false) < length)
            throw new MessageFramingException("Stream ended inside a message body.");
        try
        {
            return JsonSerializer.Deserialize<JsonRpcMessage>(body, ContractJson.Options)
                ?? throw new MessageFramingException("Empty message.");
        }
        catch (JsonException ex)
        {
            throw new MessageFramingException($"Malformed JSON: {ex.Message}");
        }
    }

    private static async Task<int> ReadFullyAsync(Stream stream, byte[] buffer, CancellationToken cancellationToken)
    {
        var total = 0;
        while (total < buffer.Length)
        {
            var n = await stream.ReadAsync(buffer.AsMemory(total), cancellationToken).ConfigureAwait(false);
            if (n == 0) break;
            total += n;
        }
        return total;
    }
}
