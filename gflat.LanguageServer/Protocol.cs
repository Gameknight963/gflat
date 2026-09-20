using System.Text;
using System.Text.Json;

namespace gflat.LanguageServer;

/// <summary>LSP's byte-counted JSON-RPC framing. Stdout is reserved for these messages.</summary>
public static class Protocol
{
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public static async Task<byte[]?> ReadAsync(Stream stream, CancellationToken cancellation = default)
    {
        var header = new List<byte>();
        byte[] one = new byte[1];
        while (true)
        {
            if (await stream.ReadAsync(one, cancellation) == 0)
            {
                if (header.Count == 0) return null;
                throw new EndOfStreamException("Truncated LSP header");
            }
            header.Add(one[0]);
            if (header.Count > 16384) throw new InvalidDataException("LSP header is too large");
            if (header.Count >= 4 && header[^4] == 13 && header[^3] == 10 && header[^2] == 13 && header[^1] == 10) break;
        }
        int? length = null;
        foreach (string line in Encoding.ASCII.GetString(header.ToArray()).Split("\r\n"))
        {
            int colon = line.IndexOf(':');
            if (colon < 0 || !line[..colon].Equals("Content-Length", StringComparison.OrdinalIgnoreCase)) continue;
            if (length != null || !int.TryParse(line[(colon + 1)..].Trim(), out int n) || n < 0 || n > 8 * 1024 * 1024)
                throw new InvalidDataException("Invalid LSP Content-Length (maximum 8 MiB)");
            length = n;
        }
        if (length == null) throw new InvalidDataException("Missing LSP Content-Length");
        byte[] body = new byte[length.Value];
        await stream.ReadExactlyAsync(body, cancellation);
        return body;
    }

    public static async Task WriteAsync(Stream stream, object message, CancellationToken cancellation = default)
    {
        byte[] body = JsonSerializer.SerializeToUtf8Bytes(message, JsonOptions);
        await stream.WriteAsync(Encoding.ASCII.GetBytes($"Content-Length: {body.Length}\r\n\r\n"), cancellation);
        await stream.WriteAsync(body, cancellation);
        await stream.FlushAsync(cancellation);
    }
}
