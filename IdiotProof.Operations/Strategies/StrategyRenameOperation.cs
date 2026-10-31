using IdiotProof.Blazor.Services;

namespace IdiotProof.Operations.Strategies;

public sealed record StrategyRenameArgs
{
    [Argument(0, Name = "strategy", Description = "Id, id prefix, stamp id or symbol.")]
    public string Strategy { get; init; } = "";

    [Argument(1, Name = "title", Description = "The new title (1-200 characters).")]
    public string Title { get; init; } = "";
}

[Operation("strategy rename", Summary = "Give a strategy a new title. Its canon, stamp id and state are untouched.", Risk = OperationRisk.Write,
    Description = "Only the title changes, so the stamp id stays the same. The Monitor's audit lines carry the title at the time, so `strategy history` matches the new title from now on.")]
[Example("idiotproof strategy rename NVDA \"NVDA VWAP reclaim\"")]
public sealed class StrategyRenameOperation : IOperation<StrategyRenameArgs>
{
    public async Task<OperationResult> ExecuteAsync(StrategyRenameArgs args, OperationContext ctx)
    {
        var title = (args.Title ?? "").Trim();
        if (title.Length is 0 or > 200) return OperationResult.Usage("A title is 1 to 200 characters.");
        if (title.StartsWith(StrategyArchive.Marker.Trim(), StringComparison.Ordinal))
            return OperationResult.Usage($"Titles can't start with {StrategyArchive.Marker.Trim()} — that marks a deleted strategy (`strategy delete`).");

        var s = await StrategyRefs.ResolveAsync(ctx, args.Strategy);
        var deleted = StrategyArchive.IsDeleted(s);
        var old = StrategyArchive.Unmark(s.Title);
        if (old == title) return OperationResult.Ok(new { s.Id, s.Title, Changed = false }, $"{s.Symbol} is already titled '{title}'.");

        s.Title = deleted ? StrategyArchive.Mark(title) : title;
        // Keep the stored canon byte-for-byte: a rename must never re-derive it from the text.
        var result = await ctx.Get<StrategyRepository>().UpdateAsync(s, s.ScriptJson, ctx.CancellationToken);
        if (result != StrategyMutation.Ok) return OperationResult.Fail($"Not renamed: {result}.");
        return OperationResult.Ok(new { s.Id, Old = old, s.Title, Changed = true },
            $"Renamed {s.Symbol} '{old}' → '{title}' ({StrategyRefs.ShortGuid(s.Id)}){(deleted ? " — still deleted" : "")}.");
    }
}
