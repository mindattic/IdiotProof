using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using IdiotProof.Core.Data;
using IdiotProof.Core.Trading;
using IdiotProof.Operations.Rendering;

using IdiotProof.Core.Services;
namespace IdiotProof.Operations.Strategies;

public enum StrategyBulkAction { Activate, Deactivate }

public sealed record StrategyBulkArgs
{
    [Argument(0, Name = "action", Description = "activate or deactivate.")]
    public StrategyBulkAction Action { get; init; }

    [Option("all", Description = "Every strategy you own (soft-deleted ones excluded).")]
    public bool All { get; init; }

    [Option("symbol", Description = "Only strategies on this symbol.")]
    public string? Symbol { get; init; }

    [Option("mode", Description = "Only strategies in this broker mode (sandbox, paper, live).")]
    public BrokerMode? Mode { get; init; }
}

[Operation("strategy bulk", Summary = "Activate or deactivate many strategies at once (--all, --symbol, --mode). Live activations need one confirmation code for the batch.", Risk = OperationRisk.Money,
    Description = "Deactivation skips strategies holding a position (that would orphan the exit). Activation skips quarantined and soft-deleted strategies; " +
                  "when the batch would turn on any Live strategy, nothing happens until you re-run with the confirmation code, which is bound to exactly that batch.")]
[Example("idiotproof strategy bulk deactivate --all")]
[Example("idiotproof strategy bulk activate --symbol NVDA --mode paper")]
public sealed class StrategyBulkOperation : IOperation<StrategyBulkArgs>
{
    /// <summary>What happened to one strategy in the batch.</summary>
    public sealed record BulkOutcome(Guid Id, string Symbol, string Title, string Mode, string Outcome);

    public async Task<OperationResult> ExecuteAsync(StrategyBulkArgs args, OperationContext ctx)
    {
        if (!args.All && string.IsNullOrWhiteSpace(args.Symbol) && args.Mode is null)
            return OperationResult.Usage("Say which strategies: --all, --symbol SYM and/or --mode MODE.");
        var owner = ctx.RequireOwner();
        var repo = ctx.Get<StrategyRepository>();
        var selected = (await repo.GetAllForUserAsync(owner, ctx.CancellationToken))
            .Where(s => !StrategyArchive.IsDeleted(s))
            .Where(s => string.IsNullOrWhiteSpace(args.Symbol) || s.Symbol.Equals(args.Symbol.Trim(), StringComparison.OrdinalIgnoreCase))
            .Where(s => args.Mode is null || OrderIntent.ParseMode(s.BrokerMode) == args.Mode)
            .OrderBy(s => s.Symbol).ThenBy(s => s.Title)
            .ToList();
        var activate = args.Action == StrategyBulkAction.Activate;
        var outcomes = new List<BulkOutcome>();
        BulkOutcome Out(Strategy s, string outcome) => new(s.Id, s.Symbol, s.Title, OrderIntent.ParseMode(s.BrokerMode).ToString(), outcome);

        var todo = new List<Strategy>();
        foreach (var s in selected)
        {
            if (s.IsActive == activate) { outcomes.Add(Out(s, activate ? "already active" : "already inactive")); continue; }
            if (activate && StrategyRefs.Load(s).Definition is null) { outcomes.Add(Out(s, "skipped: quarantined")); continue; }
            if (!activate && s.PositionQty > 0) { outcomes.Add(Out(s, $"skipped: holds {s.PositionQty}")); continue; }
            todo.Add(s);
        }

        var live = activate ? todo.Where(s => OrderIntent.ParseMode(s.BrokerMode) == BrokerMode.Live).ToList() : [];
        if (live.Count > 0)
        {
            var preview = $"ACTIVATE {todo.Count} strategies, {live.Count} of them LIVE — the Monitor trades real money when their conditions match: " +
                          string.Join(", ", live.Select(s => $"{s.Symbol} '{s.Title}'"));
            var gate = await BatchGateAsync(ctx, owner, todo.Select(s => s.Id), preview);
            if (gate is not null) return gate;
        }

        foreach (var s in todo)
        {
            var m = await repo.SetActiveAsync(s.Id, activate, owner, ctx.CancellationToken);
            outcomes.Add(Out(s, m switch
            {
                StrategyMutation.Ok => activate ? "activated" : "deactivated",
                StrategyMutation.PositionOpen => "skipped: holds a position",
                var other => $"skipped: {other}",
            }));
        }

        var changed = outcomes.Count(o => o.Outcome is "activated" or "deactivated");
        if (outcomes.Count == 0) return OperationResult.Ok(new { Changed = 0, Items = outcomes }, "No strategies match.");
        var table = new TextTable { MaxWidth = ctx.Width }.Column("id").Column("sym").Column("title", maxWidth: 34).Column("mode").Column("outcome");
        foreach (var o in outcomes) table.Row(StrategyRefs.ShortGuid(o.Id), o.Symbol, o.Title, o.Mode, o.Outcome);
        return OperationResult.Ok(new { Action = args.Action.ToString().ToLowerInvariant(), Changed = changed, Items = outcomes },
            table.Render() + $"\n{changed} {(activate ? "activated" : "deactivated")} of {outcomes.Count} matched.");
    }

    /// <summary>
    /// One confirmation code for the whole batch, bound to exactly these strategy ids (sorted), so a
    /// code issued for one batch can't turn on a different one. Null = proceed.
    /// </summary>
    private static async Task<OperationResult?> BatchGateAsync(OperationContext ctx, Guid owner, IEnumerable<Guid> ids, string preview)
    {
        var payload = JsonSerializer.Serialize(new { strategy_ids = ids.OrderBy(i => i).ToArray(), action = "bulk-activate" });
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
        var store = ctx.Get<ConfirmationStore>();
        if (string.IsNullOrWhiteSpace(ctx.ConfirmationCode))
        {
            var pending = await store.IssueAsync(owner, ConfirmationStore.KindActivateLive, payload, hash, preview, ctx.CancellationToken);
            return OperationResult.NeedsConfirmation(pending.Code, preview, pending.ExpiresUtc, new { action = "bulk-activate", count = ids.Count() });
        }
        var redemption = await store.RedeemAsync(ctx.ConfirmationCode, owner, ctx.Caller.ToString().ToLowerInvariant(),
            p => p.Kind == ConfirmationStore.KindActivateLive && p.PayloadJson == payload ? hash : "mismatch", ctx.CancellationToken);
        return redemption.Succeeded ? null : OperationResult.Blocked(redemption.Message, new { failure = redemption.Failure.ToString() });
    }
}
