using System.Text;
using System.Text.Json;

namespace IdiotProof.Operations.Streaming;

/// <summary>
/// Writes server-sent events to a response body (the Hub's <see cref="HubWire.StreamPath"/>):
/// <c>event: line</c> per pushed <see cref="StreamLine"/> and, at the end, <c>event: result</c>.
/// Writes are serialized and flushed one event at a time so a person sees each line as it happens.
/// </summary>
public sealed class SseStreamSink(Stream body) : IStreamSink
{
    private readonly SemaphoreSlim writeLock = new(1, 1);

    public ValueTask WriteAsync(StreamLine line, CancellationToken ct) =>
        new(WriteEventAsync(HubWire.StreamLineEvent, JsonSerializer.Serialize(line, OperationJson.Options), ct));

    /// <summary>One event. <paramref name="json"/> must be a single line (System.Text.Json output is).</summary>
    public async Task WriteEventAsync(string name, string json, CancellationToken ct)
    {
        var bytes = Encoding.UTF8.GetBytes($"event: {name}\ndata: {json.ReplaceLineEndings(" ")}\n\n");
        await writeLock.WaitAsync(ct);
        try
        {
            await body.WriteAsync(bytes, ct);
            await body.FlushAsync(ct);
        }
        finally { writeLock.Release(); }
    }

    /// <summary>An SSE comment line, so a quiet stream still proves the connection is alive.</summary>
    public async Task WriteHeartbeatAsync(CancellationToken ct)
    {
        await writeLock.WaitAsync(ct);
        try
        {
            await body.WriteAsync(": ping\n\n"u8.ToArray(), ct);
            await body.FlushAsync(ct);
        }
        finally { writeLock.Release(); }
    }
}
