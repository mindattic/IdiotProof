using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using IdiotProof.Models;

namespace IdiotProof.DataFeeds;

/// <summary>
/// Alpaca market-data websocket client (wss://stream.data.alpaca.markets/v2/{feed}).
/// Subscribes to trades + minute bars for a symbol set and keeps an in-memory
/// last-trade cache, raising <see cref="BarReceived"/>/<see cref="TradeReceived"/>
/// so the Monitor can evaluate the instant new data lands instead of polling.
///
/// Resilience: the receive loop survives dropped sockets with capped
/// exponential backoff and re-authenticates + re-subscribes on reconnect —
/// the same "never dies on one bad tick" philosophy as SupervisedLoop
/// (IP-LAW-5). All failures are reported through <see cref="Status"/>; the
/// caller decides whether to fall back to REST polling.
/// </summary>
public sealed class AlpacaStreamingClient : IAsyncDisposable
{
    private readonly string apiKeyId;
    private readonly string apiSecretKey;
    private readonly string feed;
    private readonly ConcurrentDictionary<string, LatestPrice> lastTrades = new(StringComparer.OrdinalIgnoreCase);
    private readonly object subscriptionLock = new();
    private HashSet<string> subscribedSymbols = new(StringComparer.OrdinalIgnoreCase);
    private ClientWebSocket? socket;
    private Task? receiveLoop;
    private CancellationTokenSource? cts;

    /// <summary>Fired when a completed minute bar arrives for a subscribed symbol.</summary>
    public event Action<Candle>? BarReceived;

    /// <summary>Fired on every trade print for a subscribed symbol.</summary>
    public event Action<LatestPrice>? TradeReceived;

    /// <summary>Human-readable connection status for logs/UI.</summary>
    public string Status { get; private set; } = "stopped";

    public bool IsConnected => socket?.State == WebSocketState.Open;

    public AlpacaStreamingClient(string apiKeyId, string apiSecretKey, string feed = "iex")
    {
        this.apiKeyId = apiKeyId ?? "";
        this.apiSecretKey = apiSecretKey ?? "";
        this.feed = string.IsNullOrWhiteSpace(feed) ? "iex" : feed.ToLowerInvariant();
    }

    /// <summary>Last trade seen for a symbol, or null before the first print.</summary>
    public LatestPrice? GetLastTrade(string symbol) =>
        lastTrades.TryGetValue(symbol, out var lp) ? lp : null;

    /// <summary>Starts the connect/receive loop. Idempotent.</summary>
    public void Start()
    {
        if (receiveLoop is not null) return;
        cts = new CancellationTokenSource();
        receiveLoop = Task.Run(() => RunAsync(cts.Token));
    }

    /// <summary>
    /// Replaces the subscribed symbol set. Applied immediately when connected;
    /// re-applied automatically after every reconnect.
    /// </summary>
    public async Task SetSymbolsAsync(IEnumerable<string> symbols, CancellationToken ct = default)
    {
        HashSet<string> next = new(symbols.Select(s => s.ToUpperInvariant()), StringComparer.OrdinalIgnoreCase);
        HashSet<string> added, removed;
        lock (subscriptionLock)
        {
            added = new HashSet<string>(next.Except(subscribedSymbols), StringComparer.OrdinalIgnoreCase);
            removed = new HashSet<string>(subscribedSymbols.Except(next), StringComparer.OrdinalIgnoreCase);
            subscribedSymbols = next;
        }
        if (!IsConnected) return; // reconnect path re-subscribes the full set

        if (added.Count > 0)
            await SendAsync(new { action = "subscribe", trades = added.ToArray(), bars = added.ToArray() }, ct).ConfigureAwait(false);
        if (removed.Count > 0)
            await SendAsync(new { action = "unsubscribe", trades = removed.ToArray(), bars = removed.ToArray() }, ct).ConfigureAwait(false);
    }

    private async Task RunAsync(CancellationToken ct)
    {
        var backoff = TimeSpan.FromSeconds(1);
        while (!ct.IsCancellationRequested)
        {
            try
            {
                socket = new ClientWebSocket();
                Status = "connecting";
                await socket.ConnectAsync(new Uri($"wss://stream.data.alpaca.markets/v2/{feed}"), ct).ConfigureAwait(false);
                await SendAsync(new { action = "auth", key = apiKeyId, secret = apiSecretKey }, ct).ConfigureAwait(false);

                HashSet<string> current;
                lock (subscriptionLock) current = new(subscribedSymbols, StringComparer.OrdinalIgnoreCase);
                if (current.Count > 0)
                    await SendAsync(new { action = "subscribe", trades = current.ToArray(), bars = current.ToArray() }, ct).ConfigureAwait(false);

                Status = "connected";
                backoff = TimeSpan.FromSeconds(1); // reset after a good connect

                await ReceiveUntilClosedAsync(socket, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                Status = $"reconnecting ({ex.GetType().Name}: {ex.Message})";
            }
            finally
            {
                socket?.Dispose();
                socket = null;
            }

            try { await Task.Delay(backoff, ct).ConfigureAwait(false); }
            catch (OperationCanceledException) { break; }
            backoff = TimeSpan.FromTicks(Math.Min(backoff.Ticks * 2, TimeSpan.FromMinutes(1).Ticks));
        }
        Status = "stopped";
    }

    private async Task ReceiveUntilClosedAsync(ClientWebSocket ws, CancellationToken ct)
    {
        var buffer = new byte[64 * 1024];
        var message = new MemoryStream();
        while (ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
        {
            message.SetLength(0);
            WebSocketReceiveResult result;
            do
            {
                result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), ct).ConfigureAwait(false);
                if (result.MessageType == WebSocketMessageType.Close)
                    return;
                message.Write(buffer, 0, result.Count);
            }
            while (!result.EndOfMessage);

            HandleMessage(Encoding.UTF8.GetString(message.GetBuffer(), 0, (int)message.Length));
        }
    }

    private void HandleMessage(string json)
    {
        // One malformed/unexpected frame must not tear the socket down — an
        // exception thrown here would bubble out of the receive loop and
        // trigger a full disconnect/reconnect cycle (dropping live coverage
        // for seconds) over a frame we were going to ignore anyway.
        try { HandleMessageCore(json); }
        catch (Exception ex)
        {
            Status = $"frame skipped ({ex.GetType().Name}: {ex.Message})";
        }
    }

    private void HandleMessageCore(string json)
    {
        // Alpaca sends a JSON array of typed events: {"T":"t"|"b"|"q"|"success"|"error"|"subscription", ...}
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Array) return;

        foreach (var el in doc.RootElement.EnumerateArray())
        {
            var type = el.TryGetProperty("T", out var t) ? t.GetString() : null;
            switch (type)
            {
                case "t": // trade
                {
                    var symbol = el.GetProperty("S").GetString() ?? "";
                    var price = el.GetProperty("p").GetDecimal();
                    var ts = el.TryGetProperty("t", out var tsEl) ? tsEl.GetDateTime() : DateTime.UtcNow;
                    var lp = new LatestPrice(symbol, price, ts, "AlpacaStream");
                    lastTrades[symbol] = lp;
                    TradeReceived?.Invoke(lp);
                    break;
                }
                case "b": // minute bar
                {
                    var symbol = el.GetProperty("S").GetString() ?? "";
                    var start = el.GetProperty("t").GetDateTime();
                    BarReceived?.Invoke(new Candle
                    {
                        Symbol = symbol,
                        StartUtc = start,
                        EndUtc = start.AddMinutes(1),
                        Open = el.GetProperty("o").GetDecimal(),
                        High = el.GetProperty("h").GetDecimal(),
                        Low = el.GetProperty("l").GetDecimal(),
                        Close = el.GetProperty("c").GetDecimal(),
                        Volume = el.GetProperty("v").GetDecimal(),
                        Note = "AlpacaStream",
                    });
                    break;
                }
                case "error":
                {
                    var msg = el.TryGetProperty("msg", out var m) ? m.GetString() : "unknown";
                    Status = $"stream error: {msg}";
                    break;
                }
            }
        }
    }

    private Task SendAsync(object payload, CancellationToken ct)
    {
        var ws = socket ?? throw new InvalidOperationException("Streaming socket not connected.");
        var bytes = JsonSerializer.SerializeToUtf8Bytes(payload);
        return ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, endOfMessage: true, ct);
    }

    public async ValueTask DisposeAsync()
    {
        cts?.Cancel();
        if (receiveLoop is not null)
        {
            try { await receiveLoop.ConfigureAwait(false); }
            catch { /* teardown */ }
        }
        socket?.Dispose();
        cts?.Dispose();
    }
}
