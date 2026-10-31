using IdiotProof.DataFeeds;
using IdiotProof.Operations.Rendering;

namespace IdiotProof.Operations.Streaming;

/// <summary>One websocket in <c>market stream status</c>.</summary>
public sealed record StreamStatusRow(
    string Stream, string Owner, string State, DateTime? ConnectedSinceUtc, DateTime? LastMessageUtc, double? QuietSeconds,
    long Messages, int Reconnects, int Watchers, string Detail, string? LastError, bool Healthy);

/// <summary>
/// <c>market stream status</c> (IP-T206): the health of every Alpaca websocket the Hub has open:
/// the Monitor's market data stream (trades and minute bars for the symbols it evaluates), the
/// trade-updates listeners <c>orders stream</c> started, and any <c>market watch</c> in progress:
/// state, how long connected, when the last message came, message and reconnect counts, and the
/// last error. Read-only.
/// </summary>
[Operation("market stream status", Summary = "Websocket health: the Monitor's market data stream, the trade-updates listeners and open market watches (state, connected since, last message, reconnects, errors).", Risk = OperationRisk.Read)]
[Example("idiotproof market stream status")]
public sealed class MarketStreamStatusOperation : IOperation<NoArgs>
{
    /// <summary>A connected stream with no message for this long is flagged quiet (bars arrive every minute in session).</summary>
    public static readonly TimeSpan QuietAfter = TimeSpan.FromMinutes(2);

    public Task<OperationResult> ExecuteAsync(NoArgs args, OperationContext ctx)
    {
        var now = ctx.UtcNow;
        var rows = new List<StreamStatusRow>();
        foreach (var c in AlpacaStreamingClient.Started)
        {
            var connected = c.IsConnected;
            rows.Add(Row($"monitor market data · {c.Feed}", "hub", c.Status, c.ConnectedSinceUtc, c.LastMessageUtc, c.Messages, c.Reconnects, 0,
                $"{c.SubscribedCount} symbol(s): trades + minute bars", connected ? null : c.Status, connected, now));
        }
        if (ctx.Services.GetService(typeof(StreamRegistry)) is StreamRegistry registry)
        {
            foreach (var h in registry.Health())
                rows.Add(Row(h.Name, ctx.OwnerId is null ? "-" : "you", h.State, h.ConnectedSinceUtc, h.LastMessageUtc, h.Messages, h.Reconnects,
                    h.Watchers, h.Detail, h.LastError, h.State == "live", now));
        }

        if (rows.Count == 0)
            return Task.FromResult(OperationResult.Ok(new { Streams = rows },
                "No Alpaca websockets are open. The Monitor's market data stream starts with the Monitor (IDIOTPROOF_STREAMING=0 or no Alpaca keys keeps it off; " +
                "it then polls REST); `orders stream` starts an account's trade-updates listener and `market watch` opens a tape."));

        var table = new TextTable { MaxWidth = ctx.Width }
            .Column("stream").Column("state").Column("connected since").Column("last message").Column("msgs", Align.Right)
            .Column("reconn", Align.Right).Column("watching", Align.Right).Column("detail", maxWidth: 48);
        foreach (var r in rows)
            table.Row(r.Stream, (r.Healthy ? "" : "! ") + r.State, r.ConnectedSinceUtc is { } since ? Fmt.EtTime(since) : "-",
                r.LastMessageUtc is { } last ? $"{Fmt.EtTime(last)} ({Ago(r.QuietSeconds)})" : "never",
                r.Messages.ToString("N0"), r.Reconnects.ToString(), r.Watchers.ToString(), r.LastError is { } e && !r.Healthy ? $"{r.Detail}; {e}" : r.Detail);
        var unhealthy = rows.Count(r => !r.Healthy);
        var summary = unhealthy == 0
            ? $"{rows.Count} stream(s), all connected (as of {Fmt.Et(now)})."
            : $"{unhealthy} of {rows.Count} stream(s) need attention (! = not connected, or quiet over {QuietAfter.TotalMinutes:0} min; quiet is normal outside market hours).";
        return Task.FromResult(OperationResult.Ok(new { AsOfUtc = now, Streams = rows }, table.Render() + "\n" + summary));
    }

    private static StreamStatusRow Row(string stream, string owner, string state, DateTime? since, DateTime? last, long messages, int reconnects,
        int watchers, string detail, string? error, bool connected, DateTime now)
    {
        double? quiet = last is { } l ? Math.Max(0, (now - l).TotalSeconds) : null;
        var healthy = connected && (quiet is null || quiet < QuietAfter.TotalSeconds);
        return new StreamStatusRow(stream, owner, state, since, last, quiet, messages, reconnects, watchers, detail, error, healthy);
    }

    private static string Ago(double? seconds) => seconds switch
    {
        null => "-",
        < 60 => $"{seconds:0}s ago",
        < 3600 => $"{seconds / 60:0}m ago",
        _ => $"{seconds / 3600:0.#}h ago",
    };
}
