using System.Text;
using System.Text.Json;

namespace IdiotProof.Operations.Streaming;

/// <summary>Alpaca refused the stream for good (bad keys, connection limit, missing subscription); reconnecting would not help.</summary>
public sealed class AlpacaStreamRefusedException(string message) : Exception(message);

/// <summary>A stream's health as <c>market stream status</c> shows it.</summary>
public sealed record AlpacaStreamHealth(
    string Name, string Endpoint, string State, DateTime? ConnectedSinceUtc, DateTime? LastMessageUtc,
    long Messages, int Reconnects, string? LastError, int Watchers, string Detail);

/// <summary>
/// One Alpaca websocket (trade updates, or a market data feed) with the protocol the Hub's
/// streaming operations share: connect, authenticate and wait for the answer, subscribe, then
/// hand every message (each element of an array frame) to the caller; on a dropped connection
/// reconnect with capped backoff and subscribe again. A refusal (bad keys, 406 connection limit,
/// 409 missing subscription) ends it with <see cref="AlpacaStreamRefusedException"/>. msgpack
/// frames (the options feed) are decoded to JSON first, so callers always read JSON.
/// </summary>
public sealed class AlpacaStreamSession
{
    /// <summary>The paper account's trade-updates stream.</summary>
    public const string PaperTradingStream = "wss://paper-api.alpaca.markets/stream";

    /// <summary>The live account's trade-updates stream.</summary>
    public const string LiveTradingStream = "wss://api.alpaca.markets/stream";

    /// <summary>Market data streams: append <c>v2/{feed}</c>, <c>v1beta3/crypto/us</c>, <c>v1beta1/{opra|indicative}</c> or <c>v1beta1/news</c>.</summary>
    public const string DataStreamBase = "wss://stream.data.alpaca.markets/";

    private readonly IStreamSocketFactory sockets;
    private readonly AlpacaStreamKeys keys;
    private readonly Func<object> subscribe;
    private readonly TimeProvider clock;
    private long messages;

    public AlpacaStreamSession(string name, Uri endpoint, AlpacaStreamKeys keys, Func<object> subscribe,
        IStreamSocketFactory sockets, TimeProvider? clock = null, bool msgpack = false)
    {
        Name = name;
        Endpoint = endpoint;
        this.keys = keys;
        this.subscribe = subscribe;
        this.sockets = sockets;
        this.clock = clock ?? TimeProvider.System;
        MsgPackFrames = msgpack;
    }

    public string Name { get; }

    public Uri Endpoint { get; }

    /// <summary>True for the options feed, which speaks MessagePack only.</summary>
    public bool MsgPackFrames { get; }

    public string State { get; private set; } = "starting";

    public DateTime? ConnectedSinceUtc { get; private set; }

    public DateTime? LastMessageUtc { get; private set; }

    public long Messages => Interlocked.Read(ref messages);

    public int Reconnects { get; private set; }

    public string? LastError { get; private set; }

    /// <summary>The longest wait between reconnect attempts.</summary>
    public TimeSpan MaxBackoff { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>The first wait after a dropped connection (doubles up to <see cref="MaxBackoff"/>).</summary>
    public TimeSpan FirstBackoff { get; init; } = TimeSpan.FromSeconds(1);

    public AlpacaStreamHealth Health(int watchers, string detail) =>
        new(Name, Endpoint.ToString(), State, ConnectedSinceUtc, LastMessageUtc, Messages, Reconnects, LastError, watchers, detail);

    /// <summary>
    /// Runs until <paramref name="ct"/> is cancelled (returns) or Alpaca refuses the stream (throws
    /// <see cref="AlpacaStreamRefusedException"/>). <paramref name="onMessage"/> sees every data
    /// message after authentication, including subscription acknowledgements.
    /// </summary>
    public async Task RunAsync(Func<JsonElement, ValueTask> onMessage, CancellationToken ct)
    {
        var backoff = FirstBackoff;
        var first = true;
        while (!ct.IsCancellationRequested)
        {
            if (!first) Reconnects++;
            first = false;
            try
            {
                State = "connecting";
                var headers = MsgPackFrames ? new Dictionary<string, string> { ["Content-Type"] = "application/msgpack" } : null;
                await using var socket = await sockets.ConnectAsync(Endpoint, headers, ct);
                State = "authenticating";
                await SendAsync(socket, new Dictionary<string, object?> { ["action"] = "auth", ["key"] = keys.KeyId, ["secret"] = keys.Secret }, ct);
                await AwaitAuthAsync(socket, ct);
                await SendAsync(socket, subscribe(), ct);
                State = "live";
                ConnectedSinceUtc = clock.GetUtcNow().UtcDateTime;
                backoff = FirstBackoff;

                while (await socket.ReceiveAsync(ct) is { } frame)
                {
                    foreach (var element in Elements(frame))
                    {
                        Interlocked.Increment(ref messages);
                        LastMessageUtc = clock.GetUtcNow().UtcDateTime;
                        if (Refusal(element) is { } refused) throw Refuse(refused);
                        await onMessage(element);
                    }
                }
                LastError = "Alpaca closed the connection.";
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (AlpacaStreamRefusedException)
            {
                throw;
            }
            catch (Exception ex)
            {
                LastError = $"{ex.GetType().Name}: {ex.Message}";
            }

            ConnectedSinceUtc = null;
            State = $"reconnecting in {backoff.TotalSeconds:0}s";
            try { await Task.Delay(backoff, clock, ct); }
            catch (OperationCanceledException) { break; }
            backoff = TimeSpan.FromTicks(Math.Min(backoff.Ticks * 2, MaxBackoff.Ticks));
        }
        State = "stopped";
        ConnectedSinceUtc = null;
    }

    private AlpacaStreamRefusedException Refuse(string why)
    {
        State = "refused";
        LastError = why;
        ConnectedSinceUtc = null;
        return new AlpacaStreamRefusedException($"Alpaca refused the {Name} stream: {why}");
    }

    /// <summary>Reads frames until Alpaca says the keys are good, or refuses them.</summary>
    private async Task AwaitAuthAsync(IStreamSocket socket, CancellationToken ct)
    {
        for (var i = 0; i < 20; i++)
        {
            var frame = await socket.ReceiveAsync(ct) ?? throw new IOException("Alpaca closed the connection during authentication.");
            foreach (var element in Elements(frame))
            {
                if (Refusal(element) is { } refused) throw Refuse(refused);
                if (IsAuthorized(element)) return;
            }
        }
        throw new IOException("No authentication answer from Alpaca.");
    }

    /// <summary>True for the "you are in" answer: market data <c>{"T":"success","msg":"authenticated"}</c>, trading <c>authorization: authorized</c>.</summary>
    internal static bool IsAuthorized(JsonElement e) =>
        (Str(e, "T") == "success" && Str(e, "msg") == "authenticated")
        || (Str(e, "stream") == "authorization" && e.TryGetProperty("data", out var d) && d.ValueKind == JsonValueKind.Object && Str(d, "status") == "authorized");

    /// <summary>Why Alpaca refused, when <paramref name="e"/> is a refusal; null otherwise.</summary>
    internal static string? Refusal(JsonElement e)
    {
        if (e.ValueKind != JsonValueKind.Object) return null;
        if (Str(e, "stream") == "authorization" && e.TryGetProperty("data", out var d) && d.ValueKind == JsonValueKind.Object
            && Str(d, "status") is { } status && status != "authorized")
            return $"authorization {status} (check the account's key pair)";
        if (Str(e, "T") != "error") return null;
        var code = e.TryGetProperty("code", out var c) && c.TryGetInt32(out var n) ? n : 0;
        var msg = Str(e, "msg") ?? "error";
        return code switch
        {
            401 or 402 or 403 => $"{code} {msg} (check the key pair)",
            406 => $"406 {msg}: Alpaca allows a limited number of connections per account and feed, and the Monitor's market data stream may hold it; use another feed (--feed iex) or another account's keys",
            409 => $"409 {msg}: the account's market data subscription does not include this feed",
            _ => null,
        };
    }

    private List<JsonElement> Elements(StreamFrame frame)
    {
        var json = MsgPackFrames && frame.Binary ? MsgPack.ToJson(frame.Data) : Encoding.UTF8.GetString(frame.Data);
        JsonElement root;
        try { root = JsonDocument.Parse(json).RootElement.Clone(); }
        catch (JsonException) { return []; }
        return root.ValueKind == JsonValueKind.Array ? [.. root.EnumerateArray()] : [root];
    }

    private Task SendAsync(IStreamSocket socket, object message, CancellationToken ct) =>
        MsgPackFrames
            ? socket.SendAsync(MsgPack.Encode(message), binary: true, ct)
            : socket.SendAsync(JsonSerializer.SerializeToUtf8Bytes(message), binary: false, ct);

    internal static string? Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}
