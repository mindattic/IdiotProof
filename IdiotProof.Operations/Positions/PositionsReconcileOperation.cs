using IdiotProof.Core.Data;
using IdiotProof.Core.Trading;
using IdiotProof.Operations.Account;
using IdiotProof.Operations.Rendering;
using IdiotProof.Operations.Strategies;

using IdiotProof.Core.Services;
namespace IdiotProof.Operations.Positions;

public sealed record PositionsReconcileArgs
{
    [Option("mode", Description = "paper, live or all (default all).")]
    public AccountScope Mode { get; init; } = AccountScope.All;

    [Option("problems", Description = "Only the rows where the broker and the strategies disagree.")]
    public bool ProblemsOnly { get; init; }

    [Option("apply", Description = "Clear phantom rows (booked but the broker is flat): book each strategy's own filled exit orders from the broker, else flat as reconciled. Never places an order.")]
    public bool Apply { get; init; }
}

/// <summary>How a symbol's broker quantity compares with what the owner's strategies book.</summary>
public enum ReconcileStatus
{
    /// <summary>The strategies book exactly what the broker holds.</summary>
    Ok,
    /// <summary>The broker holds it and no strategy books it (a manual or foreign position).</summary>
    Untracked,
    /// <summary>A strategy books shares the broker does not hold (the Monitor would try to exit nothing).</summary>
    Phantom,
    /// <summary>Both sides hold some, but different amounts.</summary>
    Mismatch,
}

/// <summary>One (account, symbol) line of the reconciliation.</summary>
public sealed record ReconcileRow(
    BrokerMode Account, string Symbol, decimal BrokerQty, int BookedQty, ReconcileStatus Status,
    IReadOnlyList<Strategy> Strategies, string AssetClass)
{
    public decimal Difference => BrokerQty - BookedQty;
}

/// <summary>The comparison itself, separate from the reads so it is easy to reason about.</summary>
public static class PositionReconciler
{
    /// <summary>
    /// Compares one account's broker positions with the strategies routed to that account
    /// (BrokerMode) that book a position. Options and crypto are never booked by strategies, so
    /// they always come back <see cref="ReconcileStatus.Untracked"/>.
    /// </summary>
    public static List<ReconcileRow> Compare(BrokerMode account, IEnumerable<BrokerPosition> positions, IEnumerable<Strategy> strategies)
    {
        var held = positions.GroupBy(p => BrokerPositions.PathSymbol(p.Symbol)).ToDictionary(g => g.Key, g => g.First());
        var booked = strategies
            .Where(s => OrderIntent.ParseMode(s.BrokerMode) == account && s.PositionQty != 0)
            .GroupBy(s => BrokerPositions.PathSymbol(s.Symbol))
            .ToDictionary(g => g.Key, g => g.ToList());

        var rows = new List<ReconcileRow>();
        foreach (var symbol in held.Keys.Union(booked.Keys).OrderBy(s => s, StringComparer.Ordinal))
        {
            held.TryGetValue(symbol, out var p);
            var owners = booked.TryGetValue(symbol, out var list) ? list : [];
            var brokerQty = p?.Quantity ?? 0m;
            var bookedQty = owners.Sum(s => s.PositionQty);
            var status = brokerQty == bookedQty ? ReconcileStatus.Ok
                : bookedQty == 0 ? ReconcileStatus.Untracked
                : brokerQty == 0 ? ReconcileStatus.Phantom
                : ReconcileStatus.Mismatch;
            rows.Add(new ReconcileRow(account, p?.Symbol ?? owners[0].Symbol.ToUpperInvariant(), brokerQty, bookedQty, status, owners, p?.AssetClass ?? "us_equity"));
        }
        return rows;
    }

    /// <summary>What to do about a row, in one line.</summary>
    public static string Advice(ReconcileRow r) => r.Status switch
    {
        ReconcileStatus.Phantom => "booked but not held — the Monitor would try to exit shares that aren't there",
        ReconcileStatus.Untracked => r.AssetClass == "us_equity" ? "held at the broker; no strategy manages its exit" : $"{r.AssetClass} — strategies don't book these",
        ReconcileStatus.Mismatch => r.Difference > 0 ? $"broker holds {Fmt.Number(r.Difference)} more than booked" : $"booked {Fmt.Number(-r.Difference)} more than the broker holds",
        _ => "",
    };
}

[Operation("positions reconcile", Summary = "What Alpaca actually holds vs what your strategies book (PositionQty), per account. --apply clears phantom rows (booked, broker flat) from the strategies' own filled exits; it never places an order.", Risk = OperationRisk.Write)]
[Example("idiotproof positions reconcile")]
[Example("idiotproof positions reconcile --mode paper --problems")]
[Example("idiotproof positions reconcile --mode paper --apply", Description = "Book each phantom as the exit its own filled sell order made, else flat as reconciled.")]
public sealed class PositionsReconcileOperation : IOperation<PositionsReconcileArgs>
{
    public async Task<OperationResult> ExecuteAsync(PositionsReconcileArgs args, OperationContext ctx)
    {
        var owner = ctx.RequireOwner();
        var strategies = await ctx.Get<StrategyRepository>().GetAllForUserAsync(owner, ctx.CancellationToken);
        var accounts = await BrokerPositions.LoadAsync(ctx, args.Mode);

        var table = new TextTable { MaxWidth = ctx.Width }
            .Column("Account").Column("Symbol").Column("Broker", Align.Right).Column("Booked", Align.Right)
            .Column("Diff", Align.Right).Column("Status").Column("Strategies", maxWidth: 30).Column("Note", maxWidth: 60);
        var data = new List<object>();
        var notes = new List<string>();
        var cleared = new List<string>();
        int ok = 0, problems = 0;
        foreach (var a in accounts)
        {
            if (a.Error is not null)
            {
                notes.Add($"{a.Name} account not compared: {a.Error}");
                data.Add(new { account = a.Name.ToLowerInvariant(), error = a.Error });
                continue;
            }
            foreach (var r in PositionReconciler.Compare(a.Mode, a.Positions, strategies))
            {
                if (args.Apply && r.Status == ReconcileStatus.Phantom && r.BrokerQty == 0m)
                {
                    foreach (var s in r.Strategies) cleared.Add(await ClearPhantomAsync(ctx, owner, a.Mode, s));
                    continue;
                }
                if (r.Status == ReconcileStatus.Ok) ok++; else problems++;
                data.Add(new
                {
                    account = a.Name.ToLowerInvariant(), r.Symbol, r.BrokerQty, r.BookedQty, r.Difference,
                    status = r.Status.ToString().ToLowerInvariant(), r.AssetClass,
                    strategies = r.Strategies.Select(s => new { s.Id, s.Title, s.PositionQty, s.IsActive }),
                });
                if (args.ProblemsOnly && r.Status == ReconcileStatus.Ok) continue;
                table.Row(a.Name, r.Symbol, Fmt.Number(r.BrokerQty), r.BookedQty.ToString(), r.Difference == 0 ? "" : Fmt.Number(r.Difference),
                    r.Status.ToString().ToLowerInvariant(),
                    string.Join(", ", r.Strategies.Select(s => $"{StrategyRefs.ShortGuid(s.Id)}={s.PositionQty}")),
                    PositionReconciler.Advice(r));
            }
        }

        var sandbox = strategies.Count(s => OrderIntent.ParseMode(s.BrokerMode) == BrokerMode.Sandbox && s.PositionQty != 0);
        if (sandbox > 0) notes.Add($"{sandbox} Sandbox strategies book positions in the simulator; they are not compared with Alpaca.");

        var summary = $"{ok} agree, {problems} disagree." + (args.Apply ? $" {cleared.Count} phantom strateg{(cleared.Count == 1 ? "y" : "ies")} cleared." : "");
        notes.AddRange(cleared);
        var parts = new List<string>();
        if (table.RowCount > 0) parts.Add(table.Render());
        else if (accounts.Any(a => a.Error is null)) parts.Add(problems == 0 && ok == 0 ? "No positions held and none booked." : "Every position agrees with its strategies.");
        parts.Add(summary);
        parts.AddRange(notes);
        if (problems > 0 && !args.Apply) parts.Add("Nothing was changed. Close a stray position with `idiotproof positions close <symbol>`; inspect one with `idiotproof positions show <symbol>`.");
        return OperationResult.Ok(new { agree = ok, disagree = problems, cleared = cleared.Count, rows = data, notes }, string.Join("\n", parts));
    }

    /// <summary>
    /// Clears one phantom (booked, broker flat): finds this strategy's own filled exit orders (client id
    /// <c>ipx-&lt;strategy id&gt;-…</c>) at the broker since its entry and books the exit at their fill price;
    /// with none, books it flat as reconciled at the entry price (P&amp;L 0). Reads orders, never places one.
    /// </summary>
    private static async Task<string> ClearPhantomAsync(OperationContext ctx, Guid owner, BrokerMode mode, Strategy s)
    {
        var ct = ctx.CancellationToken;
        var closingSide = s.PositionQty < 0 ? "buy" : "sell";
        var prefix = $"ipx-{s.Id:N}-";
        var since = (s.EntryFilledUtc ?? DateTime.UtcNow.AddDays(-2)).AddMinutes(-5);
        decimal qty = 0m, notional = 0m;
        DateTime? lastFill = null;
        string? orderId = null;
        string? lookupError = null;
        try
        {
            await using var api = await ctx.Get<AlpacaApiFactory>().ForOwnerAsync(owner, mode, ct);
            var json = await api.GetAsync("/v2/orders", new Dictionary<string, string?>
            {
                ["status"] = "all", ["symbols"] = s.Symbol.ToUpperInvariant(), ["limit"] = "500", ["nested"] = "false", ["direction"] = "asc",
                ["after"] = since.ToString("o", System.Globalization.CultureInfo.InvariantCulture),
            }, ct);
            if (json.ValueKind == System.Text.Json.JsonValueKind.Array)
                foreach (var o in json.EnumerateArray().Select(Orders.OrderView.Parse))
                {
                    if (o.ClientOrderId?.StartsWith(prefix, StringComparison.Ordinal) != true) continue;
                    if (!string.Equals(o.Side, closingSide, StringComparison.OrdinalIgnoreCase)) continue;
                    if (o.FilledQty is not > 0m || o.FilledAvgPrice is not > 0m) continue;
                    qty += o.FilledQty.Value;
                    notional += o.FilledQty.Value * o.FilledAvgPrice.Value;
                    if (o.FilledUtc is { } f && (lastFill is null || f > lastFill)) { lastFill = f; orderId = o.Id; }
                    orderId ??= o.Id;
                }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            lookupError = ex.Message;
        }

        var strategies = ctx.Get<StrategyRepository>();
        var diary = ctx.Get<TradeDiaryRepository>();
        var entry = s.LastEntryPrice ?? 0m;
        var booked = Math.Abs(s.PositionQty);
        var found = qty > 0m;
        var price = found ? Math.Round(notional / qty, 4) : entry;
        var soldQty = found ? Math.Min(booked, (int)Math.Floor(qty)) : booked;
        var exitUtc = lastFill ?? DateTime.UtcNow;
        var perShare = s.PositionQty < 0 ? entry - price : price - entry;
        var realized = found ? Math.Round(perShare * soldQty, 2) : 0m;
        var reason = found ? "Reconciled" : "ReconciledFlat";

        await strategies.RecordExitFillAsync(s.Id, price, reason, exitUtc, ct);
        try { await diary.CloseAsync(s.Id, price, reason, orderId, realized, soldQty, exitUtc, ct); }
        catch (Exception ex) when (ex is not OperationCanceledException) { /* the strategy row is already flat; the diary is best-effort */ }

        var text = found
            ? $"[{s.Title}] {s.Symbol} booked {booked} but the broker is flat: booked the exit at its own filled order {orderId} ({Fmt.Number(qty)} @ {price:0.00##}, P&L {realized:+0.00;-0.00})."
            : $"[{s.Title}] {s.Symbol} booked {booked} but the broker is flat and no filled exit of this strategy was found" +
              (lookupError is null ? "" : $" (order lookup failed: {lookupError})") + ": booked flat as reconciled, no price, P&L 0.";
        try { await ctx.Get<AuditLogRepository>().LogAsync("position-reconciled", text, owner, ct: ct); }
        catch (Exception ex) when (ex is not OperationCanceledException) { }
        return text;
    }
}
