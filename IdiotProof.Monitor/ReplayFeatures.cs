using System.Text.Json;
using IdiotProof.Blazor.Data;

namespace IdiotProof.Monitor;

/// <summary>
/// Flattens a <see cref="ReplayRun"/>'s DATA payload into the normalized ML
/// feature store rows (<see cref="ReplayTrade"/> per round-trip,
/// <see cref="ReplayBar"/> per minute). One extractor, used both when a replay
/// is saved (populate) and by replay-export (backfill any run missing rows,
/// then emit CSV from the tables) — so the SQL store and the CSVs never drift.
/// </summary>
public static class ReplayFeatures
{
    public static (List<ReplayTrade> Trades, List<ReplayBar> Bars) Extract(ReplayRun run)
    {
        var trades = new List<ReplayTrade>();
        var bars = new List<ReplayBar>();
        JsonElement root;
        try { root = JsonDocument.Parse(run.DataJson).RootElement; }
        catch { return (trades, bars); }

        int condTotal = root.TryGetProperty("conditions", out var cs) && cs.ValueKind == JsonValueKind.Array ? cs.GetArrayLength() : 0;

        var barByEt = new Dictionary<string, JsonElement>();
        if (root.TryGetProperty("bars", out var barr) && barr.ValueKind == JsonValueKind.Array)
            foreach (var b in barr.EnumerateArray())
            {
                var et = Str(b, "et");
                barByEt[et] = b;
                var passed = b.TryGetProperty("cnd", out var cnd) && cnd.ValueKind == JsonValueKind.Array
                    ? cnd.EnumerateArray().Count(x => x.ValueKind == JsonValueKind.True) : 0;
                bars.Add(new ReplayBar
                {
                    ReplayRunId = run.Id, Symbol = run.Symbol, DateEt = run.DateEt, Strategy = run.Strategy,
                    Et = et, Min = Min(et),
                    Open = Dbl(b, "o"), High = Dbl(b, "h"), Low = Dbl(b, "l"), Close = Dbl(b, "c"),
                    Volume = (long)Dbl(b, "v"), Vwap = Dbl(b, "vwap"), WindowHigh = Dbl(b, "whigh"), Volx = Dbl(b, "volx"),
                    InSession = Bit(b, "inSession"), CondPassed = passed, CondTotal = condTotal,
                    Fire = Bit(b, "fire"), Exit = Bit(b, "exit"),
                });
            }

        if (root.TryGetProperty("payoffs", out var ps) && ps.ValueKind == JsonValueKind.Array)
            foreach (var p in ps.EnumerateArray())
            {
                var entryEt = Str(p, "entryEt"); var exitEt = Str(p, "exitEt");
                var entryPx = Dbl(p, "entryPx"); var pnl = Dbl(p, "pnlPct");
                barByEt.TryGetValue(entryEt, out var eb);
                var vwap = eb.ValueKind == JsonValueKind.Object ? Dbl(eb, "vwap") : 0;
                var wh = eb.ValueKind == JsonValueKind.Object ? Dbl(eb, "whigh") : 0;
                trades.Add(new ReplayTrade
                {
                    ReplayRunId = run.Id, Symbol = run.Symbol, DateEt = run.DateEt, Strategy = run.Strategy, Feed = run.Feed,
                    EntryEt = entryEt, EntryMin = Min(entryEt), EntryPx = entryPx,
                    ExitEt = exitEt, ExitMin = Min(exitEt), HoldMin = Min(exitEt) - Min(entryEt), ExitPx = Dbl(p, "exitPx"),
                    PnlPct = pnl, Reason = Str(p, "reason"), Won = pnl > 0,
                    EntryVwap = vwap, EntryWindowHigh = wh, EntryVolx = eb.ValueKind == JsonValueKind.Object ? Dbl(eb, "volx") : 0,
                    DistVwapPct = vwap > 0 ? (entryPx - vwap) / vwap * 100 : 0,
                    DistWinHighPct = wh > 0 ? (entryPx - wh) / wh * 100 : 0,
                    GeneratedUtc = run.GeneratedUtc,
                });
            }
        return (trades, bars);
    }

    private static int Min(string et)
    {
        var parts = (et ?? "").Split(':');
        return parts.Length == 2 && int.TryParse(parts[0], out var h) && int.TryParse(parts[1], out var m) ? h * 60 + m : 0;
    }
    private static string Str(JsonElement e, string k) =>
        e.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
    private static double Dbl(JsonElement e, string k) =>
        e.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : 0;
    private static bool Bit(JsonElement e, string k) =>
        e.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.True;
}
