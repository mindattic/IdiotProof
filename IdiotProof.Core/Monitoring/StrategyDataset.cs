using System.Globalization;
using System.Text;
using System.Text.Json;
using IdiotProof.Core.Data;
using MindAttic.Export.Artifacts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using IdiotProof.Core.Services;
namespace IdiotProof.Core.Monitoring;

/// <summary>
/// ML-ready export (CLI: <c>replay-export</c>). The normalized SQL feature store
/// (<see cref="ReplayTrade"/> / <see cref="ReplayBar"/>) is authoritative; this
/// (1) backfills any <see cref="ReplayRun"/> not yet flattened into it, then
/// (2) writes two tidy CSV tables pandas / scikit-learn read directly:
///
///   trades.csv — one row per round-trip: entry-bar features → realized P&amp;L label.
///   bars.csv   — one row per minute bar: the time-series feature vector + flags.
///
/// Plus manifest.json. Output defaults to /idiotproof/dataset/. Because the CSVs
/// are emitted straight from the tables, the SQL store and the export never drift.
/// </summary>
public static class StrategyDataset
{
    /// <summary>Where the dataset is written unless a caller names another folder.</summary>
    public const string DefaultDatasetDir = @"D:\Projects\MindAttic\MindAttic.Web\mindattic.com\idiotproof\dataset";

    /// <summary>The legacy `replay-export` command: migrate, then <see cref="ExportToAsync"/>.</summary>
    public static async Task ExportAsync(IServiceProvider sp, Dictionary<string, string> opt)
    {
        var dir = opt.GetValueOrDefault("out", DefaultDatasetDir);
        var dbf = sp.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using (var db = await dbf.CreateDbContextAsync())
            await db.Database.MigrateAsync();
        var r = await ExportToAsync(dbf, dir, DateTime.UtcNow);
        Console.WriteLine($"exported {r.Runs} run(s){(r.Backfilled > 0 ? $" (+{r.Backfilled} backfilled)" : "")} → " +
                          $"{r.TradeRows} trade rows, {r.BarRows} bar rows → {dir}");
    }

    /// <summary>
    /// Backfills any run missing from the feature store, then writes trades.csv, bars.csv and
    /// manifest.json into <paramref name="dir"/> straight from the normalized tables.
    /// </summary>
    public static async Task<DatasetExportResult> ExportToAsync(IDbContextFactory<AppDbContext> dbf, string dir, DateTime nowUtc, CancellationToken ct = default)
    {
        Directory.CreateDirectory(dir);

        // (1) Backfill: flatten any run that has no bars in the store yet
        //     (older runs written before the feature store existed).
        int backfilled = 0;
        await using (var db = await dbf.CreateDbContextAsync(ct))
        {
            var withBars = (await db.ReplayBars.Select(b => b.ReplayRunId).Distinct().ToListAsync()).ToHashSet();
            foreach (var run in await db.ReplayRuns.ToListAsync())
            {
                if (withBars.Contains(run.Id)) continue;
                var (tr, br) = ReplayFeatures.Extract(run);
                db.ReplayTrades.AddRange(tr);
                db.ReplayBars.AddRange(br);
                backfilled++;
            }
            if (backfilled > 0) await db.SaveChangesAsync();
        }

        // (2) Emit CSVs straight from the normalized tables.
        List<ReplayTrade> trades; List<ReplayBar> bars; int runCount;
        await using (var db = await dbf.CreateDbContextAsync())
        {
            trades = await db.ReplayTrades.OrderBy(t => t.GeneratedUtc).ThenBy(t => t.EntryMin).ToListAsync();
            bars = await db.ReplayBars.OrderBy(b => b.Symbol).ThenBy(b => b.Min).ToListAsync();
            runCount = await db.ReplayRuns.CountAsync();
        }

        var t = new StringBuilder();
        t.AppendLine("runId,symbol,dateEt,strategy,feed,entryEt,entryMin,entryPx,exitEt,exitMin,holdMin,exitPx,pnlPct,reason,won,entryVwap,entryWindowHigh,entryVolx,distVwapPct,distWinHighPct,generatedUtc");
        foreach (var r in trades)
            t.AppendLine(string.Join(",", new[]
            {
                r.ReplayRunId.ToString("N")[..12], Csv(r.Symbol), r.DateEt, Csv(r.Strategy), r.Feed,
                r.EntryEt, r.EntryMin.ToString(), F(r.EntryPx), r.ExitEt, r.ExitMin.ToString(), r.HoldMin.ToString(), F(r.ExitPx),
                F(r.PnlPct), Csv(r.Reason), r.Won ? "1" : "0",
                F(r.EntryVwap), F(r.EntryWindowHigh), F(r.EntryVolx), F(r.DistVwapPct), F(r.DistWinHighPct),
                r.GeneratedUtc.ToString("O"),
            }));

        var b2 = new StringBuilder();
        b2.AppendLine("runId,symbol,dateEt,strategy,et,min,open,high,low,close,volume,vwap,windowHigh,volx,inSession,condPassed,condTotal,fire,exit");
        foreach (var r in bars)
            b2.AppendLine(string.Join(",", new[]
            {
                r.ReplayRunId.ToString("N")[..12], Csv(r.Symbol), r.DateEt, Csv(r.Strategy), r.Et, r.Min.ToString(),
                F(r.Open), F(r.High), F(r.Low), F(r.Close), r.Volume.ToString(),
                F(r.Vwap), F(r.WindowHigh), F(r.Volx),
                r.InSession ? "1" : "0", r.CondPassed.ToString(), r.CondTotal.ToString(), r.Fire ? "1" : "0", r.Exit ? "1" : "0",
            }));

        // Regenerated dataset: overwrite in place, exact names, UTF-8 without BOM.
        var artifact = new ArtifactOptions
        {
            Existing = ExistingArtifact.Overwrite,
            SanitizeName = false,
            Encoding = new UTF8Encoding(false),
        };
        await ArtifactWriter.WriteTextAsync(dir, "trades.csv", t.ToString(), artifact);
        await ArtifactWriter.WriteTextAsync(dir, "bars.csv", b2.ToString(), artifact);
        var manifest = new
        {
            generatedUtc = DateTime.SpecifyKind(nowUtc, DateTimeKind.Utc).ToString("O"),
            runs = runCount, tradeRows = trades.Count, barRows = bars.Count, backfilledRuns = backfilled,
            source = "SQL ReplayTrades / ReplayBars (normalized feature store)",
            files = new[] { "trades.csv", "bars.csv" },
            note = "trades.csv = one row per round-trip (entry features -> pnlPct/won label). bars.csv = one row per minute bar (time-series features + entry/exit flags).",
        };
        await ArtifactWriter.WriteTextAsync(dir, "manifest.json",
            JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }), artifact);

        return new DatasetExportResult(runCount, trades.Count, bars.Count, backfilled, dir,
            [Path.Combine(dir, "trades.csv"), Path.Combine(dir, "bars.csv"), Path.Combine(dir, "manifest.json")]);
    }

    private static string F(double d) => d.ToString("0.####", CultureInfo.InvariantCulture);
    private static string Csv(string s)
    {
        s ??= "";
        return s.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0 ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;
    }
}

/// <summary>What a dataset export wrote: row counts, runs backfilled into the feature store, and the files.</summary>
public sealed record DatasetExportResult(int Runs, int TradeRows, int BarRows, int Backfilled, string Directory, IReadOnlyList<string> Files);
