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
    private readonly Dictionary<string, HashSet<string>> symbolsByOwner = new(StringComparer.Ordinal);
    private HashSet<string> subscribedSymbols = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The subscription owner <see cref="SetSymbolsAsync(IEnumerable{string}, CancellationToken)"/> writes: the Monitor's armed symbols.</summary>
    public const string DefaultOwner = "monitor";
    private ClientWebSocket? socket;
    private Task? receiveLoop;
    private CancellationTokenSource? cts;
    private long messages;

    // Every started client, so `market stream status` (IP-T206) can report the Monitor's socket.
    private static readonly ConcurrentDictionary<AlpacaStreamingClient, byte> started = new();

    /// <summary>The clients started in this process and not yet disposed (the Monitor's, normally one).</summary>
    public static IReadOnlyList<AlpacaStreamingClient> Started => [.. started.Keys];

    /// <summary>The data feed this client streams (sip or iex).</summary>
    public string Feed => feed;

    /// <summary>How many symbols are subscribed (trades and minute bars).</summary>
    public int SubscribedCount
    {
        get { lock (subscriptionLock) return subscribedSymbols.Count; }
    }

    /// <summary>When the current connection authenticated and subscribed (UTC), or null while down.</summary>
    public DateTime? ConnectedSinceUtc { get; private set; }

    /// <summary>When the last frame arrived (UTC).</summary>
    public DateTime? LastMessageUtc { get; private set; }

    /// <summary>Frames received since start.</summary>
    public long Messages => Interlocked.Read(ref messages);

    /// <summary>Connections opened after the first one.</summary>
    public int Reconnects { get; private set; }

    /// <summary>Fired when a completed minute bar arrives for a subscribed symbol.</summary>
    public event Action<Candle>? BarReceived;

    /// <summary>Fired on every trade print for a subscribed symbol.</summary>
    public event Action<LatestPrice>? TradeReceived;

    /// <summary>Fired on every trading-status message (<c>statuses</c> channel: halts, resumptions, pauses) for a subscribed symbol.</summary>
    public event Action<TradingStatus>? StatusReceived;

    /// <summary>Fired on every limit-up/limit-down band message (<c>lulds</c> channel) for a subscribed symbol.</summary>
    public event Action<LuldBand>? LuldReceived;

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
        started.TryAdd(this, 0);
        cts = new CancellationTokenSource();
        receiveLoop = Task.Run(() => RunAsync(cts.Token));
    }

    /// <summary>
    /// Replaces the Monitor's (<see cref="DefaultOwner"/>) symbol set. Applied immediately when
    /// connected; re-applied automatically after every reconnect.
    /// </summary>
    public Task SetSymbolsAsync(IEnumerable<string> symbols, CancellationToken ct = default) =>
        SetSymbolsAsync(DefaultOwner, symbols, ct);

    /// <summary>
    /// Replaces one owner's symbol set; the socket subscribes to the union over every owner, so
    /// the Monitor's armed symbols and the formation recorder's universe share the one SIP
    /// connection the account allows. An empty set drops the owner.
    /// </summary>
    public async Task SetSymbolsAsync(string owner, IEnumerable<string> symbols, CancellationToken ct = default)
    {
        HashSet<string> added, removed;
        lock (subscriptionLock)
        {
            var mine = new HashSet<string>(symbols.Select(s => s.ToUpperInvariant()), StringComparer.OrdinalIgnoreCase);
            if (mine.Count == 0) symbolsByOwner.Remove(owner);
            else symbolsByOwner[owner] = mine;
            var next = new HashSet<string>(symbolsByOwner.Values.SelectMany(v => v), StringComparer.OrdinalIgnoreCase);
            added = new HashSet<string>(next.Except(subscribedSymbols), StringComparer.OrdinalIgnoreCase);
            removed = new HashSet<string>(subscribedSymbols.Except(next), StringComparer.OrdinalIgnoreCase);
            subscribedSymbols = next;
        }
        if (!IsConnected) return; // reconnect path re-subscribes the full set

        if (added.Count > 0)
            await SendAsync(Subscription("subscribe", added), ct).ConfigureAwait(false);
        if (removed.Count > 0)
            await SendAsync(Subscription("unsubscribe", removed), ct).ConfigureAwait(false);
    }

    /// <summary>The symbols one owner asked for (empty for an unknown owner).</summary>
    public IReadOnlyCollection<string> SymbolsOf(string owner)
    {
        lock (subscriptionLock) return symbolsByOwner.TryGetValue(owner, out var s) ? [.. s] : [];
    }

    private static object Subscription(string action, IEnumerable<string> symbols)
    {
        var list = symbols.ToArray();
        return new { action, trades = list, bars = list, statuses = list, lulds = list };
    }

    private async Task RunAsync(CancellationToken ct)
    {
        var backoff = TimeSpan.FromSeconds(1);
        var first = true;
        while (!ct.IsCancellationRequested)
        {
            if (!first) Reconnects++;
            first = false;
            try
            {
                socket = new ClientWebSocket();
                Status = "connecting";
                await socket.ConnectAsync(new Uri($"wss://stream.data.alpaca.markets/v2/{feed}"), ct).ConfigureAwait(false);
                await SendAsync(new { action = "auth", key = apiKeyId, secret = apiSecretKey }, ct).ConfigureAwait(false);

                HashSet<string> current;
                lock (subscriptionLock) current = new(subscribedSymbols, StringComparer.OrdinalIgnoreCase);
                if (current.Count > 0)
                    await SendAsync(Subscription("subscribe", current), ct).ConfigureAwait(false);

                Status = "connected";
                var connectedAt = DateTime.UtcNow;
                ConnectedSinceUtc = connectedAt;

                await ReceiveUntilClosedAsync(socket, ct).ConfigureAwait(false);

                // Reset the backoff only after a connection that actually held. An auth refusal or a
                // "connection limit exceeded" closes within seconds; resetting on connect made those
                // retry every second forever.
                if (DateTime.UtcNow - connectedAt >= TimeSpan.FromSeconds(30))
                    backoff = TimeSpan.FromSeconds(1);
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
                ConnectedSinceUtc = null;
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

            Interlocked.Increment(ref messages);
            LastMessageUtc = DateTime.UtcNow;
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
                case "s": // trading status
                {
                    if (ParseStatus(el) is { } status) StatusReceived?.Invoke(status);
                    break;
                }
                case "l": // LULD band
                {
                    if (ParseLuld(el) is { } band) LuldReceived?.Invoke(band);
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

    /// <summary>A <c>{"T":"s", ...}</c> frame as a <see cref="TradingStatus"/>; null when it names no symbol.</summary>
    public static TradingStatus? ParseStatus(JsonElement el)
    {
        var symbol = Str(el, "S");
        if (string.IsNullOrWhiteSpace(symbol)) return null;
        var ts = el.TryGetProperty("t", out var tsEl) && tsEl.ValueKind == JsonValueKind.String && tsEl.TryGetDateTime(out var t) ? t.ToUniversalTime() : DateTime.UtcNow;
        return new TradingStatus(symbol.ToUpperInvariant(), Str(el, "sc") ?? "", Str(el, "sm") ?? "", Str(el, "rc") ?? "", Str(el, "rm") ?? "", ts, Str(el, "z") ?? "");
    }

    /// <summary>A <c>{"T":"l", ...}</c> frame as a <see cref="LuldBand"/>; null when it names no symbol.</summary>
    public static LuldBand? ParseLuld(JsonElement el)
    {
        var symbol = Str(el, "S");
        if (string.IsNullOrWhiteSpace(symbol)) return null;
        var ts = el.TryGetProperty("t", out var tsEl) && tsEl.ValueKind == JsonValueKind.String && tsEl.TryGetDateTime(out var t) ? t.ToUniversalTime() : DateTime.UtcNow;
        decimal? up = el.TryGetProperty("u", out var u) && u.ValueKind == JsonValueKind.Number ? u.GetDecimal() : null;
        decimal? down = el.TryGetProperty("d", out var d) && d.ValueKind == JsonValueKind.Number ? d.GetDecimal() : null;
        return new LuldBand(symbol.ToUpperInvariant(), up, down, Str(el, "i") ?? "", ts, Str(el, "z") ?? "");
    }

    private static string? Str(JsonElement el, string name) =>
        el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private Task SendAsync(object payload, CancellationToken ct)
    {
        var ws = socket ?? throw new InvalidOperationException("Streaming socket not connected.");
        var bytes = JsonSerializer.SerializeToUtf8Bytes(payload);
        return ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, endOfMessage: true, ct);
    }

    public async ValueTask DisposeAsync()
    {
        started.TryRemove(this, out _);
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
