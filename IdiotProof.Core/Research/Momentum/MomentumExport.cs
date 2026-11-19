using System.Globalization;
using System.Text.Json;
using IdiotProof.Core.Services;
using Microsoft.EntityFrameworkCore;

namespace IdiotProof.Core.Research.Momentum;

/// <summary>
/// The "Momentum. calls vs reality" data (IP-T514): every scenario outcome, brag and video in the compact shape the page
/// template and tools/momentum/edge read (short keys, ET times, rounded numbers). It used to be queried straight from
/// SQL by build-artifact.ps1; it is read here so the Hub stays the only process talking to the database.
/// </summary>
public sealed partial class MomentumTracker
{
    public async Task<MomentumRunResult> ExportAsync(string outPath, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var videos = await db.MomentumVideos.AsNoTracking().ToListAsync(ct);
        var vmap = videos.ToDictionary(v => v.VideoId, StringComparer.Ordinal);

        var picks = await (from c in db.MomentumClaims.AsNoTracking()
                           join o in db.MomentumClaimOutcomes.AsNoTracking() on c.Id equals o.ClaimId
                           where c.ClaimType == PickWatchlist || c.ClaimType == PickMorning
                           orderby c.TradeDate descending, c.VideoId, c.Rank, o.ScenarioIndex
                           select new { c, o }).ToListAsync(ct);
        var brags = await (from c in db.MomentumClaims.AsNoTracking()
                           join o in db.MomentumClaimOutcomes.AsNoTracking() on c.Id equals o.ClaimId
                           where c.ClaimType == Brag
                           orderby c.TradeDate descending
                           select new { c, o }).ToListAsync(ct);
        var total = await db.MomentumTranscripts.CountAsync(ct);
        var withText = await db.MomentumTranscripts.CountAsync(t => t.Text != null, ct);
        var extracted = await db.MomentumTranscripts.CountAsync(t => t.ExtractedUtc != null, ct);

        static string? Et(DateTime? utc, string format) => utc is { } u ? EasternTime.FromUtc(u).ToString(format, CultureInfo.InvariantCulture) : null;
        static double? R(decimal? x, int d) => x is { } v ? Math.Round((double)v, d) : null;
        static double? Rd(double? x, int d) => x is { } v ? Math.Round(v, d) : null;
        static string? Clip(string? s, int n) => string.IsNullOrEmpty(s) ? null : s.Length <= n ? s : s[..n].TrimEnd() + "...";

        var scenarioCache = new Dictionary<Guid, List<StoredScenario>>();
        var calls = new List<Dictionary<string, object?>>();
        foreach (var x in picks)
        {
            if (!scenarioCache.TryGetValue(x.c.Id, out var list)) scenarioCache[x.c.Id] = list = Scenarios(x.c);
            var sc = x.o.ScenarioIndex < list.Count ? list[x.o.ScenarioIndex] : null;
            var targets = new List<object>();
            var hits = TargetHits(x.o.TargetsJson);
            if (hits.Count > 0) targets.AddRange(hits.Select(h => new { p = h.Level is { } l ? Math.Round(l, 4) : (double?)null, t = Et(h.HitUtc, "HH:mm") }));
            else if (sc is not null) targets.AddRange(sc.Targets.Where(t => t.Price is not null).Select(t => new { p = R(t.Price, 4), t = (string?)null }));
            var pub = vmap.TryGetValue(x.c.VideoId, out var v) ? Et(v.PublishedUtc, "yyyy-MM-dd HH:mm") : null;
            calls.Add(new Dictionary<string, object?>
            {
                ["d"] = x.c.TradeDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), ["vid"] = x.c.VideoId, ["pub"] = pub,
                ["k"] = x.c.ClaimType == PickMorning ? "am" : "wl", ["rk"] = x.c.Rank, ["sym"] = x.c.Symbol, ["sp"] = x.c.SpokenSymbol,
                ["tags"] = Tags(x.c), ["nm"] = sc?.Name, ["q"] = Clip(sc?.Quote ?? x.c.RawQuote, 420), ["v"] = x.o.Verdict,
                ["pc"] = R(x.o.PriorClose, 4), ["op"] = R(x.o.Open0930, 4), ["pmh"] = R(x.o.PremarketHigh, 4),
                ["hi"] = R(x.o.DayHigh, 4), ["cl"] = R(x.o.DayClose, 4), ["hiP"] = Rd(x.o.DayHighPct, 1), ["clP"] = Rd(x.o.DayClosePct, 1),
                ["gr"] = x.o.GainerRank, ["top"] = x.o.TopGapper, ["pr"] = x.o.PrevDayGainerRank, ["prP"] = Rd(x.o.PrevDayClosePct, 1),
                ["tr"] = R(x.o.TriggerLevel, 4), ["trT"] = Et(x.o.TriggerUtc, "HH:mm"),
                ["en"] = R(x.o.EntryPrice, 4), ["enT"] = Et(x.o.EntryUtc, "HH:mm"),
                ["ex"] = R(x.o.ExitPrice, 4), ["exT"] = Et(x.o.ExitUtc, "HH:mm"), ["xr"] = x.o.ExitReason,
                ["pnl"] = Rd(x.o.PlanPnlPct, 2), ["run"] = Rd(x.o.MaxRunAfterTriggerPct, 1), ["dd"] = Rd(x.o.MaxDrawdownAfterTriggerPct, 1),
                ["tg"] = targets, ["th"] = x.o.TargetsHit, ["ft"] = x.o.FirstTargetHit,
                ["st"] = R(x.o.InvalidationLevel, 4), ["stS"] = x.o.InvalidationSource, ["n"] = Clip(x.o.Notes, 200),
                ["why"] = Clip(string.Join(" | ", new[] { x.c.IdentityHint, x.c.RawQuote }.Where(s => s is not null)), 600),
                ["smv"] = Rd(x.c.StatedMovePct, 0),
                ["cat"] = x.c.CatalystType, ["catT"] = Clip(x.c.Catalyst, 200),
            });
        }
        var bragRows = brags.Select(x => new Dictionary<string, object?>
        {
            ["d"] = x.c.TradeDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), ["vid"] = x.c.VideoId, ["sym"] = x.c.Symbol, ["sp"] = x.c.SpokenSymbol,
            ["cp"] = Rd(x.c.ClaimedPct, 0), ["w"] = x.c.ClaimWindow, ["v"] = x.o.Verdict, ["ah"] = Rd(x.o.BragActualHighPct, 0),
            ["ac"] = Rd(x.o.BragActualClosePct, 0), ["alh"] = Rd(x.o.BragActualLowToHighPct, 0), ["q"] = Clip(x.c.RawQuote, 260),
        }).ToList();
        var data = new Dictionary<string, object?>
        {
            ["generatedEt"] = EasternTime.FromUtc(NowUtc).ToString("yyyy-MM-dd h:mm tt", CultureInfo.InvariantCulture) + " ET",
            ["transcripts"] = new { total, withText, extracted },
            ["videos"] = videos.Select(v => new { id = v.VideoId, t = v.Title, k = v.Kind, d = v.TradeDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) }),
            ["calls"] = calls,
            ["brags"] = bragRows,
        };
        var json = JsonSerializer.Serialize(data).Replace("�", "");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outPath))!);
        await File.WriteAllTextAsync(outPath, json, new System.Text.UTF8Encoding(false), ct);
        return new(true, $"momentum export: {calls.Count} scenarios, {bragRows.Count} brags, {videos.Count} videos -> {outPath}",
            new { scenarios = calls.Count, brags = bragRows.Count, videos = videos.Count, path = outPath });
    }

    private static List<(double? Level, DateTime? HitUtc)> TargetHits(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.EnumerateArray().Select(e => (
                e.TryGetProperty("level", out var l) && l.ValueKind == JsonValueKind.Number ? l.GetDouble() : (double?)null,
                e.TryGetProperty("hitUtc", out var h) && h.ValueKind == JsonValueKind.String && h.TryGetDateTime(out var t) ? t.ToUniversalTime() : (DateTime?)null)).ToList();
        }
        catch (JsonException) { return []; }
    }
}
