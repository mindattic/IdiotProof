using IdiotProof.Blazor.Data;
using IdiotProof.Blazor.Services;
using IdiotProof.Core.Trading;

namespace IdiotProof.Operations.Strategies;

/// <summary>
/// The soft-delete marker (HOUSE-LAW-2: soft-disable, never hard-delete). A deleted strategy is
/// deactivated and its title carries this prefix; its canon, history and bookkeeping stay, and
/// <c>strategy delete --restore</c> takes the marker off again.
/// </summary>
public static class StrategyArchive
{
    public const string Marker = "[deleted] ";

    public static bool IsDeleted(Strategy s) => s.Title.StartsWith(Marker, StringComparison.Ordinal);

    public static string Mark(string title)
    {
        var t = Marker + title;
        return t.Length > 200 ? t[..200] : t;
    }

    public static string Unmark(string title) => title.StartsWith(Marker, StringComparison.Ordinal) ? title[Marker.Length..] : title;
}

public sealed record StrategyDeleteArgs
{
    [Argument(0, Name = "strategy", Description = "Id, id prefix, stamp id or symbol.")]
    public string Strategy { get; init; } = "";

    [Option("restore", Description = "Undo a delete: take the [deleted] marker off (it stays inactive).")]
    public bool Restore { get; init; }

    [Option("hard", Description = "Permanently remove an ALREADY deleted strategy and its progress row. Cannot be undone.")]
    public bool Hard { get; init; }
}

[Operation("strategy delete", Summary = "Soft-delete a strategy: deactivate it and mark it [deleted] (reversible with --restore). Refused while it holds a position.", Risk = OperationRisk.Destructive,
    Description = "Deleting is a reversible disable (HOUSE-LAW-2): the row, canon and history stay, and `strategy list` hides it unless --deleted. " +
                  "--hard permanently removes a strategy that is already deleted — a separate, explicit second step.")]
[Example("idiotproof strategy delete NVDA")]
[Example("idiotproof strategy delete 0193ab12 --restore")]
[Example("idiotproof strategy delete 0193ab12 --hard")]
public sealed class StrategyDeleteOperation : IOperation<StrategyDeleteArgs>
{
    public async Task<OperationResult> ExecuteAsync(StrategyDeleteArgs args, OperationContext ctx)
    {
        if (args.Restore && args.Hard) return OperationResult.Usage("Pass --restore or --hard, not both.");
        var owner = ctx.RequireOwner();
        var s = await StrategyRefs.ResolveAsync(ctx, args.Strategy);
        var repo = ctx.Get<StrategyRepository>();
        var name = $"{s.Symbol} '{StrategyArchive.Unmark(s.Title)}'";

        if (s.PositionQty > 0)
            return OperationResult.Blocked($"{name} holds {s.PositionQty} shares — deleting it would orphan the exit. Flatten first " +
                $"(`idiotproof trade sell {s.Symbol} {s.PositionQty} --mode {OrderIntent.ParseMode(s.BrokerMode).ToString().ToLowerInvariant()}`).");

        if (args.Restore)
        {
            if (!StrategyArchive.IsDeleted(s)) return OperationResult.Ok(new { s.Id, Deleted = false }, $"{name} isn't deleted.");
            s.Title = StrategyArchive.Unmark(s.Title);
            return await SaveAsync(repo, s, ctx.CancellationToken, new { s.Id, Deleted = false, s.Title },
                $"Restored {name} ({StrategyRefs.ShortGuid(s.Id)}). It is inactive; `idiotproof strategy activate {StrategyRefs.ShortGuid(s.Id)}` turns it back on.");
        }

        if (args.Hard)
        {
            if (!StrategyArchive.IsDeleted(s))
                return OperationResult.Blocked($"{name} isn't deleted. Delete it first (`idiotproof strategy delete {StrategyRefs.ShortGuid(s.Id)}`); --hard only removes an already-deleted strategy.");
            return await repo.DeleteAsync(s.Id, owner, ctx.CancellationToken) switch
            {
                StrategyMutation.Ok => OperationResult.Ok(new { s.Id, Removed = true }, $"Permanently removed {name} ({StrategyRefs.ShortGuid(s.Id)})."),
                StrategyMutation.PositionOpen => OperationResult.Blocked($"{name} holds a position; flatten first."),
                StrategyMutation.NotOwner => OperationResult.Blocked("That strategy belongs to another user."),
                _ => OperationResult.Fail("Strategy not found."),
            };
        }

        if (StrategyArchive.IsDeleted(s) && !s.IsActive)
            return OperationResult.Ok(new { s.Id, Deleted = true }, $"{name} is already deleted.");

        // Deactivate through the guarded path first (ownership + open-position guard, IP-LAW-11).
        if (s.IsActive)
        {
            var off = await repo.SetActiveAsync(s.Id, false, owner, ctx.CancellationToken);
            if (off == StrategyMutation.PositionOpen) return OperationResult.Blocked($"{name} holds a position; flatten first.");
            if (off != StrategyMutation.Ok) return OperationResult.Fail($"Couldn't deactivate it: {off}.");
            s.IsActive = false;
        }
        if (!StrategyArchive.IsDeleted(s)) s.Title = StrategyArchive.Mark(s.Title);
        return await SaveAsync(repo, s, ctx.CancellationToken, new { s.Id, Deleted = true, s.Title },
            $"Deleted {name} ({StrategyRefs.ShortGuid(s.Id)}): inactive and hidden from `strategy list`. Undo with `idiotproof strategy delete {StrategyRefs.ShortGuid(s.Id)} --restore`.");
    }

    private static async Task<OperationResult> SaveAsync(StrategyRepository repo, Strategy s, CancellationToken ct, object data, string human)
    {
        // Title-only change: pass the stored canon so it is kept byte-for-byte (never re-derived from text).
        var result = await repo.UpdateAsync(s, s.ScriptJson, ct);
        return result == StrategyMutation.Ok ? OperationResult.Ok(data, human) : OperationResult.Fail($"Not saved: {result}.");
    }
}
