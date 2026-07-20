using System.Globalization;
using System.Text;
using System.Text.Json;
using IdiotProof.Blazor.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace IdiotProof.Monitor;

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
    public static async Task ExportAsync(IServiceProvider sp, Dictionary<string, string> opt)
    {
        var dir = opt.GetValueOrDefault("out", @"D:\Projects\MindAttic\mindattic.com\idiotproof\dataset");
        Directory.CreateDirectory(dir);
        var dbf = sp.GetRequiredService<IDbContextFactory<AppDbContext>>();

        // (1) Backfill: flatten any run that has no bars in the store yet
        //     (older runs written before the feature store existed).
        int backfilled = 0;
        await using (var db = await dbf.CreateDbContextAsync())
        {
            await db.Database.MigrateAsync();
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

        var enc = new UTF8Encoding(false);
        await File.WriteAllTextAsync(Path.Combine(dir, "trades.csv"), t.ToString(), enc);
        await File.WriteAllTextAsync(Path.Combine(dir, "bars.csv"), b2.ToString(), enc);
        var manifest = new
        {
            generatedUtc = DateTime.UtcNow.ToString("O"),
            runs = runCount, tradeRows = trades.Count, barRows = bars.Count, backfilledRuns = backfilled,
            source = "SQL ReplayTrades / ReplayBars (normalized feature store)",
            files = new[] { "trades.csv", "bars.csv" },
            note = "trades.csv = one row per round-trip (entry features -> pnlPct/won label). bars.csv = one row per minute bar (time-series features + entry/exit flags).",
        };
        await File.WriteAllTextAsync(Path.Combine(dir, "manifest.json"),
            JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }), enc);

        Console.WriteLine($"exported {runCount} run(s){(backfilled > 0 ? $" (+{backfilled} backfilled)" : "")} → " +
                          $"{trades.Count} trade rows, {bars.Count} bar rows → {dir}");
    }

    private static string F(double d) => d.ToString("0.####", CultureInfo.InvariantCulture);
    private static string Csv(string s)
    {
        s ??= "";
        return s.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0 ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;
    }
}
