using System.Threading.Channels;
using IdiotProof.Core.Trading;
using IdiotProof.Operations.Account;

namespace IdiotProof.Operations.Streaming;

public sealed record OrdersStreamArgs
{
    [Option("mode", Description = "paper, live or all (default all: every account with keys).")]
    public AccountScope Mode { get; init; } = AccountScope.All;

    [Option("fills-only", Description = "Show only fills and partial fills.")]
    public bool FillsOnly { get; init; }

    [Option("seconds", Description = "Stop after this many seconds (a terminal streams until Ctrl+C; MCP/--json default 15, at most 300).")]
    public int? Seconds { get; init; }

    [Option("max", Description = "Stop after this many events.")]
    public int? Max { get; init; }
}

/// <summary>
/// <c>orders stream</c> (IP-T325): Alpaca's <c>trade_updates</c> websocket for the owner's paper
/// and/or live account, hosted in the Hub. The Hub starts one listener per account on first use and
/// keeps it until it stops, writing every fill and partial fill to the audit log and the trade
/// diary (<see cref="FillRecorder"/>) whether or not anyone is watching; this operation shows the
/// events live. Nothing here places, changes or cancels an order.
/// </summary>
[Operation("orders stream", Summary = "Live order events from Alpaca (new, fill, partial fill, canceled, replaced, rejected, expired); the Hub records every fill in the trade diary and audit log.", Risk = OperationRisk.Read,
    Description = "Starts (or joins) the Hub's trade_updates listener for each account; once started it keeps recording fills until the Hub stops. A terminal streams events until Ctrl+C; MCP and --json get the events of a bounded window (--seconds, default 15) plus the last few seen.")]
[Streaming]
[Example("idiotproof orders stream")]
[Example("idiotproof orders stream --mode paper --fills-only")]
public sealed class OrdersStreamOperation : IOperation<OrdersStreamArgs>
{
    public async Task<OperationResult> ExecuteAsync(OrdersStreamArgs args, OperationContext ctx)
    {
        if (args.Seconds is < 1 or > 86_400) return OperationResult.Usage("--seconds must be 1-86400.");
        if (args.Max is < 1) return OperationResult.Usage("--max must be at least 1.");
        var owner = ctx.RequireOwner();
        var registry = ctx.Get<StreamRegistry>();
        BrokerMode[] modes = args.Mode switch
        {
            AccountScope.Paper => [BrokerMode.Paper],
            AccountScope.Live => [BrokerMode.Live],
            _ => [BrokerMode.Paper, BrokerMode.Live],
        };

        var listeners = new List<TradeUpdatesListener>();
        var skipped = new List<string>();
        foreach (var mode in modes)
        {
            try { listeners.Add(await registry.TradeUpdatesAsync(owner, mode, ctx.CancellationToken)); }
            catch (AlpacaAccessException ex)
            {
                skipped.Add($"{mode.ToString().ToLowerInvariant()}: {ex.Message}");
            }
        }
        if (listeners.Count == 0)
            return OperationResult.Blocked("No Alpaca account to stream: " + string.Join("; ", skipped));

        using var stream = OperationStream.Open(ctx, args.Seconds, args.Max);
        var accounts = string.Join(", ", listeners.Select(l => l.Mode.ToString().ToLowerInvariant()));
        if (stream.Live)
        {
            await stream.EmitAsync($"Alpaca trade updates for {accounts} (Ctrl+C stops; the Hub keeps recording fills)");
            foreach (var s in skipped) await stream.EmitAsync($"  skipped {s}");
        }

        var subscriptions = listeners.Select(l => (Listener: l, Sub: l.Subscribe())).ToList();
        var recent = listeners.SelectMany(l => l.Recent).Where(u => !args.FillsOnly || u.IsFill).OrderBy(u => u.AtUtc).TakeLast(20).ToList();
        try
        {
            await stream.RunAsync(ct => Task.WhenAll(subscriptions.Select(s => PumpAsync(s.Listener, s.Sub.Reader, stream, args.FillsOnly, ct))));
        }
        finally
        {
            foreach (var s in subscriptions) s.Sub.Subscription.Dispose();
        }

        var header = $"Alpaca trade updates for {accounts}" + (skipped.Count == 0 ? "" : $" (skipped {string.Join("; ", skipped)})") +
                     $"\nListener: {string.Join("; ", listeners.Select(l => $"{l.Mode.ToString().ToLowerInvariant()} {l.Session.State}, {l.FillsRecorded} fill(s) recorded"))}" +
                     (recent.Count == 0 ? "" : "\nSeen before this window:\n" + string.Join("\n", recent.Select(u => u.Line()))) +
                     "\nThis window:";
        return stream.Finish(header, new
        {
            Accounts = listeners.Select(l => new { Mode = l.Mode.ToString().ToLowerInvariant(), l.Session.State, l.FillsRecorded, l.Failure }),
            Skipped = skipped,
            Recent = recent,
        });
    }

    private static async Task PumpAsync(TradeUpdatesListener listener, ChannelReader<TradeUpdate> reader, OperationStream stream, bool fillsOnly, CancellationToken ct)
    {
        await foreach (var update in reader.ReadAllAsync(ct))
        {
            if (fillsOnly && !update.IsFill) continue;
            await stream.EmitAsync(update.Line(), update);
        }
        if (listener.Failure is { } why) await stream.EmitAsync($"{listener.Mode.ToString().ToLowerInvariant()} stream ended: {why}");
    }
}
