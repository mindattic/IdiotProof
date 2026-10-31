using System.Net.WebSockets;

namespace IdiotProof.Operations.Streaming;

/// <summary>One frame read from a <see cref="IStreamSocket"/>: its bytes and whether it was sent as binary.</summary>
public sealed record StreamFrame(byte[] Data, bool Binary);

/// <summary>
/// A websocket as the Hub's Alpaca streams use it: send a frame, read the next whole frame. The
/// seam tests replace with a scripted socket, so no test ever opens a real connection.
/// </summary>
public interface IStreamSocket : IAsyncDisposable
{
    Task SendAsync(byte[] data, bool binary, CancellationToken ct);

    /// <summary>The next complete frame, or null once the other side closed.</summary>
    Task<StreamFrame?> ReceiveAsync(CancellationToken ct);
}

/// <summary>Opens <see cref="IStreamSocket"/>s (a real <see cref="ClientWebSocket"/> in the Hub, a fake in tests).</summary>
public interface IStreamSocketFactory
{
    Task<IStreamSocket> ConnectAsync(Uri uri, IReadOnlyDictionary<string, string>? headers, CancellationToken ct);
}

/// <summary>The real transport: <see cref="ClientWebSocket"/>, frames reassembled up to 16 MB.</summary>
public sealed class ClientWebSocketFactory : IStreamSocketFactory
{
    public async Task<IStreamSocket> ConnectAsync(Uri uri, IReadOnlyDictionary<string, string>? headers, CancellationToken ct)
    {
        var ws = new ClientWebSocket();
        ws.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);
        foreach (var (name, value) in headers ?? new Dictionary<string, string>()) ws.Options.SetRequestHeader(name, value);
        try
        {
            await ws.ConnectAsync(uri, ct);
        }
        catch
        {
            ws.Dispose();
            throw;
        }
        return new Socket(ws);
    }

    private sealed class Socket(ClientWebSocket ws) : IStreamSocket
    {
        private const int MaxFrame = 16 * 1024 * 1024;
        private readonly byte[] buffer = new byte[64 * 1024];

        public Task SendAsync(byte[] data, bool binary, CancellationToken ct) =>
            ws.SendAsync(data, binary ? WebSocketMessageType.Binary : WebSocketMessageType.Text, endOfMessage: true, ct);

        public async Task<StreamFrame?> ReceiveAsync(CancellationToken ct)
        {
            using var message = new MemoryStream();
            while (true)
            {
                var result = await ws.ReceiveAsync(buffer, ct);
                if (result.MessageType == WebSocketMessageType.Close) return null;
                message.Write(buffer, 0, result.Count);
                if (message.Length > MaxFrame) throw new InvalidOperationException("Stream frame over 16 MB.");
                if (result.EndOfMessage) return new StreamFrame(message.ToArray(), result.MessageType == WebSocketMessageType.Binary);
            }
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                if (ws.State == WebSocketState.Open)
                {
                    using var bye = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                    await ws.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "bye", bye.Token);
                }
            }
            catch (Exception ex) when (ex is WebSocketException or OperationCanceledException or ObjectDisposedException) { }
            ws.Dispose();
        }
    }
}
