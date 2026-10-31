using System.Globalization;
using System.Text.Json;
using System.Threading.Channels;
using IdiotProof.Core.Data;
using IdiotProof.Core.Trading;
using IdiotProof.Operations.Rendering;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using IdiotProof.Core.Services;
namespace IdiotProof.Operations.Streaming;

/// <summary>One event from an account's Alpaca <c>trade_updates</c> stream (new, fill, partial_fill, canceled, replaced, rejected...).</summary>
public sealed record TradeUpdate(
    string Account, string Event, DateTime AtUtc, string OrderId, string? ClientOrderId, string Symbol, string Side,
    decimal? Qty, decimal? Price, decimal? FilledQty, decimal? OrderQty, decimal? FilledAvgPrice, decimal? PositionQty,
    string? OrderType, string? OrderStatus, string? ExecutionId)
{
    /// <summary>A fill or partial fill: the events the Hub records in the diary and audit log.</summary>
    public bool IsFill => Event is "fill" or "partial_fill";

    /// <summary>Parses a <c>{"stream":"trade_updates","data":{...}}</c> message; null for anything else.</summary>
    public static TradeUpdate? Parse(JsonElement message, string account)
    {
        if (AlpacaStreamSession.Str(message, "stream") != "trade_updates"
            || !message.TryGetProperty("data", out var d) || d.ValueKind != JsonValueKind.Object
            || !d.TryGetProperty("order", out var o) || o.ValueKind != JsonValueKind.Object)
            return null;
        var at = AlpacaStreamSession.Str(d, "timestamp") is { } ts && DateTime.TryParse(ts, CultureInfo.InvariantCulture,
            DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var parsed) ? parsed : DateTime.UtcNow;
        return new TradeUpdate(
            account,
            AlpacaStreamSession.Str(d, "event") ?? "unknown",
            at,
            AlpacaStreamSession.Str(o, "id") ?? "",
            AlpacaStreamSession.Str(o, "client_order_id"),
            AlpacaStreamSession.Str(o, "symbol") ?? "",
            AlpacaStreamSession.Str(o, "side") ?? "",
            Num(d, "qty"), Num(d, "price"), Num(o, "filled_qty"), Num(o, "qty"), Num(o, "filled_avg_price"), Num(d, "position_qty"),
            AlpacaStreamSession.Str(o, "type"), AlpacaStreamSession.Str(o, "status"),
            AlpacaStreamSession.Str(d, "execution_id"));
    }

    /// <summary>Alpaca sends quantities and prices as strings; numbers are accepted too.</summary>
    internal static decimal? Num(JsonElement e, string name)
    {
        if (!e.TryGetProperty(name, out var v)) return null;
        return v.ValueKind switch
        {
            JsonValueKind.Number when v.TryGetDecimal(out var n) => n,
            JsonValueKind.String when decimal.TryParse(v.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var n) => n,
            _ => null,
        };
    }

    /// <summary><c>9:31:07 AM ET  paper  FILL          BUY 10 AAPL @ $187.20  10/10 filled, avg $187.20  order 1a2b3c4d</c>.</summary>
    public string Line()
    {
        var at = EasternClock.ToEastern(AtUtc).ToString("h:mm:ss tt", CultureInfo.InvariantCulture) + " ET";
        var what = $"{Side.ToUpperInvariant()} {(IsFill && Qty is { } q ? Fmt.Number(q) : OrderQty is { } oq ? Fmt.Number(oq) : "")} {Symbol}".Replace("  ", " ");
        if (IsFill && Price is { } px) what += $" @ {Fmt.Money(px, px < 1 ? 4 : 2)}";
        var fill = FilledQty is { } f && OrderQty is { } total
            ? $"  {Fmt.Number(f)}/{Fmt.Number(total)} filled" + (FilledAvgPrice is { } avg && f > 0 ? $", avg {Fmt.Money(avg, avg < 1 ? 4 : 2)}" : "")
            : "";
        var id = OrderId.Length > 8 ? OrderId[..8] : OrderId;
        return $"{at}  {Account,-5}  {Event.ToUpperInvariant().Replace('_', ' '),-16} {what}{fill}  order {id}";
    }
}

/// <summary>
/// Writes every fill and partial fill from a trade-updates stream to the audit log (category
/// <c>fill</c>) and the trade diary (IP-T325, tying in the gateway's IP-T93 diary): a row whose
/// entry or exit order is this order gets the real fill price and size; a finished order nobody
/// recorded (placed in Alpaca's own app, say) opens a manual row on a buy or closes the open manual
/// row it exits. An unmatched fill waits <see cref="MatchGrace"/> first, because the gateway and
/// the Monitor write their own row just after the broker accepts the order and a fast fill can
/// arrive before that. A sell with nothing to close is audited only, never guessed into a short.
/// Duplicate events (same execution) are written once. Failures are logged, never thrown.
/// </summary>
public sealed class FillRecorder(
    IDbContextFactory<AppDbContext> dbFactory, AuditLogRepository audit, TradeDiaryRepository diary, ILogger<FillRecorder>? logger = null)
{
    /// <summary>How long an unmatched final fill waits for the gateway's or Monitor's own diary row.</summary>
    public TimeSpan MatchGrace { get; init; } = TimeSpan.FromSeconds(5);

    private readonly HashSet<string> seen = [];
    private readonly Queue<string> order = new();

    /// <summary>Records one update when it is a fill; returns what was done to the diary (for the stream line and tests).</summary>
    public async Task<string?> RecordAsync(Guid owner, bool isPaper, TradeUpdate u, CancellationToken ct = default)
    {
        if (!u.IsFill) return null;
        var key = u.ExecutionId ?? $"{u.OrderId}:{u.Event}:{u.FilledQty}";
        lock (seen)
        {
            if (!seen.Add(key)) return "duplicate";
            order.Enqueue(key);
            if (order.Count > 5000) seen.Remove(order.Dequeue());
        }

        try
        {
            var partial = u.Event == "partial_fill";
            var data = JsonSerializer.Serialize(new
            {
                source = "trade_updates", account = u.Account, @event = u.Event, execution_id = u.ExecutionId,
                order_id = u.OrderId, client_order_id = u.ClientOrderId, symbol = u.Symbol, side = u.Side,
                qty = u.Qty, price = u.Price, filled_qty = u.FilledQty, order_qty = u.OrderQty,
                filled_avg_price = u.FilledAvgPrice, position_qty = u.PositionQty, at_utc = u.AtUtc,
            });
            var price = u.Price is { } p ? Fmt.Money(p, p < 1 ? 4 : 2) : "?";
            await audit.LogReturningIdAsync("fill",
                $"{(partial ? "Partial fill" : "Fill")}: {u.Side.ToUpperInvariant()} {Fmt.Number(u.Qty ?? 0)} {u.Symbol} @ {price} " +
                $"({Fmt.Number(u.FilledQty ?? 0)}/{Fmt.Number(u.OrderQty ?? 0)} filled) · {u.Account} · order {u.OrderId}",
                owner, data, ct);

            var done = await UpdateMatchingAsync(owner, isPaper, u, ct);
            if (done is not null || partial) return done ?? "audited";
            if (MatchGrace > TimeSpan.Zero) await Task.Delay(MatchGrace, ct);
            return await UpdateMatchingAsync(owner, isPaper, u, ct) ?? await RecordUnmatchedAsync(owner, isPaper, u, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger?.LogWarning(ex, "Fill record failed for order {Order} (the order is unaffected).", u.OrderId);
            return "failed: " + ex.Message;
        }
    }

    /// <summary>Puts the fill's real price and size on the diary row that placed (or exited with) this order.</summary>
    private async Task<string?> UpdateMatchingAsync(Guid owner, bool isPaper, TradeUpdate u, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(u.OrderId)) return null;
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var row = await db.TradeDiary
            .Where(t => t.OwnerUserId == owner && t.IsPaper == isPaper && (t.EntryOrderId == u.OrderId || t.ExitOrderId == u.OrderId))
            .OrderByDescending(t => t.EntryUtc)
            .FirstOrDefaultAsync(ct);
        if (row is null) return null;

        var avg = u.FilledAvgPrice ?? u.Price;
        if (row.EntryOrderId == u.OrderId)
        {
            if (avg is > 0) row.EntryPrice = avg.Value;
            if (u.FilledQty is > 0) row.Quantity = (int)Math.Ceiling(u.FilledQty.Value);
        }
        else
        {
            if (avg is > 0) row.ExitPrice = avg.Value;
            if (row.Status == TradeDiaryStatus.Closed && row.ExitPrice is { } exit && row.EntryPrice > 0m)
            {
                var perShare = row.Direction == nameof(IdiotProof.Models.TradeDirection.Short) ? row.EntryPrice - exit : exit - row.EntryPrice;
                row.RealizedPnL = Math.Round(perShare * row.Quantity, 2);
                var cost = row.EntryPrice * row.Quantity;
                row.ReturnPercent = cost > 0m ? Math.Round(row.RealizedPnL.Value / cost * 100m, 4) : 0m;
            }
        }
        row.UpdatedUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return row.EntryOrderId == u.OrderId ? "diary entry updated" : "diary exit updated";
    }

    /// <summary>A finished order nobody recorded: a buy opens a manual row, a sell closes the open manual long it exits.</summary>
    private async Task<string> RecordUnmatchedAsync(Guid owner, bool isPaper, TradeUpdate u, CancellationToken ct)
    {
        var symbol = u.Symbol.Trim().ToUpperInvariant();
        if (symbol.Length > 20) symbol = symbol[..20];
        var avg = u.FilledAvgPrice ?? u.Price;
        var qty = (int)Math.Ceiling(u.FilledQty ?? u.Qty ?? 0);
        if (u.Side.Equals("buy", StringComparison.OrdinalIgnoreCase))
        {
            await diary.RecordManualEntryAsync(new TradeDiaryEntry
            {
                StrategyId = Guid.Empty,
                OwnerUserId = owner,
                StrategyTitle = "manual · alpaca · trade_updates",
                Symbol = symbol,
                Direction = nameof(IdiotProof.Models.TradeDirection.Long),
                Broker = "Alpaca",
                IsPaper = isPaper,
                EntryUtc = u.AtUtc,
                EntryPrice = avg ?? 0m,
                Quantity = qty,
                EntryOrderId = u.OrderId,
            }, ct);
            return "diary entry opened";
        }
        return await diary.CloseManualAsync(owner, symbol, isPaper, avg, u.OrderId, qty, u.AtUtc, "Fill (trade_updates)", ct)
            ? "diary row closed"
            : "audited (no diary row to close)";
    }
}

/// <summary>
/// The Hub-hosted trade-updates stream of one owner's account (IP-T325): started by the first
/// <c>orders stream</c> for that account and kept running until the Hub stops, so every fill is
/// recorded once by <see cref="FillRecorder"/> however many terminals are watching. Watchers
/// subscribe for a live copy of every update; <see cref="Recent"/> keeps the last few.
/// </summary>
public sealed class TradeUpdatesListener : IAsyncDisposable
{
    private const int RecentKept = 50;
    private readonly Guid owner;
    private readonly bool isPaper;
    private readonly FillRecorder recorder;
    private readonly CancellationTokenSource stop = new();
    private readonly List<Channel<TradeUpdate>> watchers = [];
    private readonly LinkedList<TradeUpdate> recent = new();
    private Task? run;

    public TradeUpdatesListener(Guid owner, BrokerMode mode, AlpacaStreamSession session, FillRecorder recorder)
    {
        this.owner = owner;
        isPaper = mode == BrokerMode.Paper;
        Mode = mode;
        Session = session;
        this.recorder = recorder;
    }

    public BrokerMode Mode { get; }

    public AlpacaStreamSession Session { get; }

    /// <summary>Why the stream ended for good (Alpaca refused it), or null while it runs.</summary>
    public string? Failure { get; private set; }

    private readonly List<Task> pending = [];
    private int fillsRecorded;

    /// <summary>Fills written so far.</summary>
    public int FillsRecorded => Volatile.Read(ref fillsRecorded);

    public bool Running => run is { IsCompleted: false };

    public IReadOnlyList<TradeUpdate> Recent
    {
        get { lock (recent) return [.. recent]; }
    }

    public int Watchers
    {
        get { lock (watchers) return watchers.Count; }
    }

    public void Start() => run ??= Task.Run(RunAsync);

    private async Task RunAsync()
    {
        try
        {
            await Session.RunAsync(OnMessageAsync, stop.Token);
        }
        catch (AlpacaStreamRefusedException ex)
        {
            Failure = ex.Message;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Failure = ex.Message;
        }
        lock (watchers)
            foreach (var w in watchers) w.Writer.TryComplete();
    }

    private async ValueTask OnMessageAsync(JsonElement message)
    {
        if (TradeUpdate.Parse(message, isPaper ? "paper" : "live") is not { } update) return;
        lock (recent)
        {
            recent.AddLast(update);
            if (recent.Count > RecentKept) recent.RemoveFirst();
        }
        lock (watchers)
            foreach (var w in watchers) w.Writer.TryWrite(update);
        if (update.IsFill)
        {
            // Off the receive loop: an unmatched fill waits for the gateway's own diary row first.
            var recording = Task.Run(async () =>
            {
                var done = await recorder.RecordAsync(owner, isPaper, update, stop.Token);
                if (done is not null && done != "duplicate" && !done.StartsWith("failed", StringComparison.Ordinal))
                    Interlocked.Increment(ref fillsRecorded);
            });
            lock (pending)
            {
                pending.RemoveAll(t => t.IsCompleted);
                pending.Add(recording);
            }
        }
        await Task.CompletedTask;
    }

    /// <summary>Waits for fills still being written (tests; shutdown).</summary>
    public async Task DrainAsync()
    {
        Task[] waiting;
        lock (pending) waiting = [.. pending];
        try { await Task.WhenAll(waiting); } catch (OperationCanceledException) { }
    }

    /// <summary>A live copy of every update from now on; dispose the subscription to stop. Completes when the stream ends.</summary>
    public (ChannelReader<TradeUpdate> Reader, IDisposable Subscription) Subscribe()
    {
        var channel = Channel.CreateBounded<TradeUpdate>(new BoundedChannelOptions(1000) { FullMode = BoundedChannelFullMode.DropOldest });
        lock (watchers)
        {
            if (run is { IsCompleted: true }) channel.Writer.TryComplete();
            else watchers.Add(channel);
        }
        return (channel.Reader, new Unsubscribe(this, channel));
    }

    private sealed class Unsubscribe(TradeUpdatesListener listener, Channel<TradeUpdate> channel) : IDisposable
    {
        public void Dispose()
        {
            lock (listener.watchers) listener.watchers.Remove(channel);
            channel.Writer.TryComplete();
        }
    }

    public AlpacaStreamHealth Health() =>
        Session.Health(Watchers, $"{FillsRecorded} fill(s) recorded" + (Failure is null ? "" : $"; ended: {Failure}"));

    public async ValueTask DisposeAsync()
    {
        stop.Cancel();
        if (run is not null)
        {
            try { await run; } catch { /* stopping */ }
        }
        await DrainAsync();
        stop.Dispose();
    }
}
