using System.Globalization;
using System.Text;
using System.Text.Json;
using IdiotProof.Blazor.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace IdiotProof.Monitor;

/// <summary>
/// ML-ready export (CLI: <c>replay-export</c>). Flattens every <see cref="ReplayRun"/>
/// stored in SQL into two tidy CSV feature/label tables that pandas / scikit-learn
/// read directly — turning the archive's per-run JSON blobs into a training set:
///
///   trades.csv — one row per round-trip: entry-bar features → realized P&amp;L label.
///   bars.csv   — one row per minute bar: the time-series feature vector + entry/exit flags.
///
/// Plus manifest.json (row counts, columns, generated ET). Output defaults to
/// /idiotproof/dataset/ so it publishes alongside the replay archive.
/// </summary>
public static class StrategyDataset
{
    public static async Task ExportAsync(IServiceProvider sp, Dictionary<string, string> opt)
    {
        var dir = opt.GetValueOrDefault("out", @"D:\Projects\MindAttic\mindattic.com\idiotproof\dataset");
        Directory.CreateDirectory(dir);

        var dbf = sp.GetRequiredService<IDbContextFactory<AppDbContext>>();
        List<ReplayRun> runs;
        await using (var db = await dbf.CreateDbContextAsync())
        {
            await db.Database.MigrateAsync();
            runs = await db.ReplayRuns.OrderBy(r => r.GeneratedUtc).ToListAsync();
        }

        var trades = new StringBuilder();
        trades.AppendLine(string.Join(",", new[]
        {
            "runId","symbol","dateEt","strategy","feed","repeat","side",
            "entryEt","entryMin","entryPx","exitEt","exitMin","holdMin","exitPx",
            "pnlPct","reason","won",
            "entryVwap","entryWindowHigh","entryVolx","distVwapPct","distWinHighPct",
            "generatedEt",
        }));

        var bars = new StringBuilder();
        bars.AppendLine(string.Join(",", new[]
        {
            "runId","symbol","dateEt","strategy","et","min",
            "open","high","low","close","volume","vwap","windowHigh","volx",
            "inSession","condPassed","condTotal","fire","exit",
        }));

        int tradeRows = 0, barRows = 0;
        foreach (var run in runs)
        {
            JsonElement root;
            try { root = JsonDocument.Parse(run.DataJson).RootElement; }
            catch { continue; }

            var rid = run.Id.ToString("N")[..12];
            var repeatFlag = root.TryGetProperty("repeat", out var rp) && rp.ValueKind == JsonValueKind.True ? 1 : 0;
            var side = Str(root, "side");
            var condTotal = root.TryGetProperty("conditions", out var cs) && cs.ValueKind == JsonValueKind.Array ? cs.GetArrayLength() : 0;

            // Index bars by ET time so a payoff can pull its entry-bar features.
            var barByEt = new Dictionary<string, JsonElement>();
            if (root.TryGetProperty("bars", out var barr) && barr.ValueKind == JsonValueKind.Array)
            {
                foreach (var b in barr.EnumerateArray())
                {
                    var et = Str(b, "et");
                    barByEt[et] = b;
                    var passed = b.TryGetProperty("cnd", out var cnd) && cnd.ValueKind == JsonValueKind.Array
                        ? cnd.EnumerateArray().Count(x => x.ValueKind == JsonValueKind.True) : 0;
                    bars.AppendLine(string.Join(",", new[]
                    {
                        rid, Csv(run.Symbol), run.DateEt, Csv(run.Strategy), et, Min(et).ToString(),
                        Num(b,"o"), Num(b,"h"), Num(b,"l"), Num(b,"c"), Num(b,"v"),
                        Num(b,"vwap"), Num(b,"whigh"), Num(b,"volx"),
                        Bit(b,"inSession"), passed.ToString(), condTotal.ToString(), Bit(b,"fire"), Bit(b,"exit"),
                    }));
                    barRows++;
                }
            }

            if (root.TryGetProperty("payoffs", out var ps) && ps.ValueKind == JsonValueKind.Array)
                foreach (var p in ps.EnumerateArray())
                {
                    var entryEt = Str(p, "entryEt"); var exitEt = Str(p, "exitEt");
                    var entryPx = Dbl(p, "entryPx"); var exitPx = Dbl(p, "exitPx");
                    var pnl = Dbl(p, "pnlPct");
                    barByEt.TryGetValue(entryEt, out var eb);
                    var vwap = eb.ValueKind == JsonValueKind.Object ? Dbl(eb, "vwap") : 0;
                    var wh = eb.ValueKind == JsonValueKind.Object ? Dbl(eb, "whigh") : 0;
                    var volx = eb.ValueKind == JsonValueKind.Object ? Dbl(eb, "volx") : 0;
                    trades.AppendLine(string.Join(",", new[]
                    {
                        rid, Csv(run.Symbol), run.DateEt, Csv(run.Strategy), run.Feed, repeatFlag.ToString(), Csv(side),
                        entryEt, Min(entryEt).ToString(), F(entryPx), exitEt, Min(exitEt).ToString(),
                        (Min(exitEt)-Min(entryEt)).ToString(), F(exitPx),
                        F(pnl), Csv(Str(p,"reason")), pnl > 0 ? "1" : "0",
                        F(vwap), F(wh), F(volx),
                        F(vwap > 0 ? (entryPx-vwap)/vwap*100 : 0), F(wh > 0 ? (entryPx-wh)/wh*100 : 0),
                        run.GeneratedEt,
                    }));
                    tradeRows++;
                }
        }

        var enc = new UTF8Encoding(false);
        await File.WriteAllTextAsync(Path.Combine(dir, "trades.csv"), trades.ToString(), enc);
        await File.WriteAllTextAsync(Path.Combine(dir, "bars.csv"), bars.ToString(), enc);
        var manifest = new
        {
            generatedUtc = DateTime.UtcNow.ToString("O"),
            runs = runs.Count, tradeRows, barRows,
            files = new[] { "trades.csv", "bars.csv" },
            note = "Flattened from SQL ReplayRuns. trades.csv = one row per round-trip (entry features -> pnlPct/won label). bars.csv = one row per minute bar (time-series features + entry/exit flags).",
        };
        await File.WriteAllTextAsync(Path.Combine(dir, "manifest.json"),
            JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }), enc);

        Console.WriteLine($"exported {runs.Count} run(s) → {tradeRows} trade rows, {barRows} bar rows → {dir}");
    }

    // ── helpers ──
    private static int Min(string et) // "HH:mm" → minutes since midnight
    {
        var parts = (et ?? "").Split(':');
        return parts.Length == 2 && int.TryParse(parts[0], out var h) && int.TryParse(parts[1], out var m) ? h * 60 + m : 0;
    }
    private static string Str(JsonElement e, string k) =>
        e.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
    private static double Dbl(JsonElement e, string k) =>
        e.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : 0;
    private static string Num(JsonElement e, string k) => F(Dbl(e, k));
    private static string Bit(JsonElement e, string k) =>
        e.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.True ? "1" : "0";
    private static string F(double d) => d.ToString("0.####", CultureInfo.InvariantCulture);
    // CSV-quote a field if it contains comma/quote/newline.
    private static string Csv(string s)
    {
        s ??= "";
        return s.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0 ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;
    }
}
