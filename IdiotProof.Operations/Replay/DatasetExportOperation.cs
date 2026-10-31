using IdiotProof.Blazor.Data;
using IdiotProof.Monitor;
using IdiotProof.Operations.Rendering;
using Microsoft.EntityFrameworkCore;

namespace IdiotProof.Operations.Replay;

public sealed record DatasetExportArgs
{
    [Option("out", Description = "Folder to write trades.csv, bars.csv and manifest.json (default the mindattic.com dataset folder).", Short = 'o')]
    public string? Out { get; init; }
}

/// <summary>
/// The ML-ready export (the old <c>replay-export</c>): backfills any replay run missing from the
/// feature store, then writes trades.csv (one row per round-trip: entry features → P&amp;L label),
/// bars.csv (one row per minute bar) and manifest.json straight from SQL. Local files only.
/// </summary>
[Operation("dataset export", Summary = "Export the replay feature store as ML-ready CSVs (trades.csv, bars.csv, manifest.json), backfilling older runs first.", Risk = OperationRisk.Write)]
[Example("idiotproof dataset export")]
[Example("idiotproof dataset export --out C:\\data\\idiotproof")]
public sealed class DatasetExportOperation : IOperation<DatasetExportArgs>
{
    public async Task<OperationResult> ExecuteAsync(DatasetExportArgs args, OperationContext ctx)
    {
        var dir = string.IsNullOrWhiteSpace(args.Out) ? StrategyDataset.DefaultDatasetDir : args.Out.Trim();
        var r = await StrategyDataset.ExportToAsync(ctx.Get<IDbContextFactory<AppDbContext>>(), dir, ctx.UtcNow, ctx.CancellationToken);
        var kv = new KeyValueBlock()
            .Add("runs", r.Runs + (r.Backfilled > 0 ? $" ({r.Backfilled} backfilled into the feature store)" : ""))
            .Add("trades.csv", $"{r.TradeRows} rows (one per round-trip)")
            .Add("bars.csv", $"{r.BarRows} rows (one per minute bar)")
            .Add("folder", r.Directory);
        return OperationResult.Ok(r, kv.Render());
    }
}
