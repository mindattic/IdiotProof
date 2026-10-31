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

[Operation("positions reconcile", Summary = "Read-only report: what Alpaca actually holds vs what your strategies book (PositionQty), per account.", Risk = OperationRisk.Read)]
[Example("idiotproof positions reconcile")]
[Example("idiotproof positions reconcile --mode paper --problems")]
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

        var summary = $"{ok} agree, {problems} disagree.";
        var parts = new List<string>();
        if (table.RowCount > 0) parts.Add(table.Render());
        else if (accounts.Any(a => a.Error is null)) parts.Add(problems == 0 && ok == 0 ? "No positions held and none booked." : "Every position agrees with its strategies.");
        parts.Add(summary);
        parts.AddRange(notes);
        if (problems > 0) parts.Add("Nothing was changed. Close a stray position with `idiotproof positions close <symbol>`; inspect one with `idiotproof positions show <symbol>`.");
        return OperationResult.Ok(new { agree = ok, disagree = problems, rows = data, notes }, string.Join("\n", parts));
    }
}
