using System.Diagnostics;
using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Nodes;
using IdiotProof.Core.Evidence;
using IdiotProof.Evidence.Economics;
using IdiotProof.Evidence.Gappers;
using Microsoft.EntityFrameworkCore;

namespace IdiotProof.Core.Research.Gappers;

public readonly record struct QuoteTick(DateTime TimeUtc, double Bid, double Ask);

/// <summary>Historical NBBO quotes for one symbol over a UTC window.</summary>
public interface IAlpacaQuoteSource
{
    Task<IReadOnlyList<QuoteTick>> GetQuotesAsync(string symbol, DateTime fromUtc, DateTime toUtc, int limit, CancellationToken ct);
}

/// <summary><c>GET /v2/stocks/{symbol}/quotes</c> on the data host, paged, rate-gated, on the frozen feed.</summary>
public sealed class AlpacaQuoteSource(StudyKeys keys, string feed, AlpacaDataRateGate gate) : IAlpacaQuoteSource, IDisposable
{
    private readonly HttpClient http = Build(keys);

    private static HttpClient Build(StudyKeys keys)
    {
        var client = new HttpClient { BaseAddress = new Uri("https://data.alpaca.markets/"), Timeout = TimeSpan.FromMinutes(2) };
        client.DefaultRequestHeaders.Add("APCA-API-KEY-ID", keys.KeyId);
        client.DefaultRequestHeaders.Add("APCA-API-SECRET-KEY", keys.Secret);
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return client;
    }

    public async Task<IReadOnlyList<QuoteTick>> GetQuotesAsync(string symbol, DateTime fromUtc, DateTime toUtc, int limit, CancellationToken ct)
    {
        var result = new List<QuoteTick>();
        string? page = null;
        do
        {
            await gate.WaitAsync(ct);
            var url = $"v2/stocks/{Uri.EscapeDataString(symbol)}/quotes?start={fromUtc:yyyy-MM-ddTHH:mm:ss.fffZ}&end={toUtc:yyyy-MM-ddTHH:mm:ss.fffZ}&limit={Math.Clamp(limit, 1, 10000)}&feed={feed}"
                + (page is null ? "" : "&page_token=" + Uri.EscapeDataString(page));
            using var doc = JsonDocument.Parse(await http.GetStringAsync(url, ct));
            if (doc.RootElement.TryGetProperty("quotes", out var quotes) && quotes.ValueKind == JsonValueKind.Array)
                foreach (var q in quotes.EnumerateArray())
                {
                    if (!q.TryGetProperty("t", out var t) || !q.TryGetProperty("bp", out var bp) || !q.TryGetProperty("ap", out var ap)) continue;
                    if (!DateTime.TryParse(t.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var when)) continue;
                    result.Add(new QuoteTick(when, bp.GetDouble(), ap.GetDouble()));
                }
            page = doc.RootElement.TryGetProperty("next_page_token", out var next) && next.ValueKind == JsonValueKind.String ? next.GetString() : null;
        } while (page is not null && result.Count < limit);
        return result;
    }

    public void Dispose() => http.Dispose();
}

/// <summary>
/// Where the frozen cost model's premarket spread multiplier comes from: a number in params.json,
/// or, while params says "calibrated-once", <c>data/spread-multiplier.json</c> written by the
/// calibration stage. Null means the cost model cannot run yet.
/// </summary>
public static class GapperSpreadMultiplier
{
    public const string FileName = "spread-multiplier.json";

    public static string File(GapperStudyPaths paths) => Path.Combine(paths.DataDir, FileName);

    public static (double? Multiplier, string Source) Resolve(GapperStudyPaths paths, GapperStudyParams p)
    {
        if (p.CostModel.Spread.ValueKind == JsonValueKind.Object && p.CostModel.Spread.TryGetProperty("premarketSpreadMultiplier", out var m))
        {
            if (m.ValueKind == JsonValueKind.Number) return (m.GetDouble(), "params.json");
        }
        var file = File(paths);
        if (!System.IO.File.Exists(file)) return (null, $"{FileName} missing: run `research gappers calibrate-spread`");
        var doc = JsonNode.Parse(System.IO.File.ReadAllText(file)) as JsonObject;
        var value = doc?["multiplier"]?.GetValue<double>();
        return value is { } v && double.IsFinite(v) && v > 0 ? (v, FileName) : (null, $"{FileName} holds no usable multiplier");
    }

    /// <summary>The frozen calibration recipe (params.costModel.spread.calibration).</summary>
    public sealed record Recipe(int Events, string Split, string Tier, string AtEt, int ToleranceSeconds, string Statistic, string Order);

    public static Recipe RecipeOf(GapperStudyParams p)
    {
        var spread = p.CostModel.Spread;
        if (spread.ValueKind != JsonValueKind.Object || !spread.TryGetProperty("calibration", out var c) || c.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("params.json has no costModel.spread.calibration block.");
        return new Recipe(
            c.GetProperty("events").GetInt32(),
            c.GetProperty("split").GetString() ?? "train",
            c.TryGetProperty("tier", out var tier) ? tier.GetString() ?? "Tier20" : "Tier20",
            c.GetProperty("atEt").GetString() ?? "08:00",
            c.TryGetProperty("toleranceSeconds", out var tol) ? tol.GetInt32() : 60,
            c.TryGetProperty("statistic", out var st) ? st.GetString() ?? "median" : "median",
            c.TryGetProperty("order", out var order) ? order.GetString() ?? "" : "");
    }

    public static double? TradeNotional(GapperStudyParams p) =>
        p.CostModel.Impact.ValueKind == JsonValueKind.Object && p.CostModel.Impact.TryGetProperty("tradeNotionalUsd", out var n) ? n.GetDouble() : null;

    public static double ImpactConstant(GapperStudyParams p) =>
        p.CostModel.Impact.ValueKind == JsonValueKind.Object && p.CostModel.Impact.TryGetProperty("impactConstant", out var k) ? k.GetDouble() : 1.0;
}

public sealed record SpreadCalibrationRequest(string? Split, int? Events, bool Force, Guid? Owner);

public sealed record SpreadCalibrationResult(double? Multiplier, string Statistic, int EventsRequested, int EventsSampled, int EventsUsed, int QuoteCalls, string File, string Note, double Minutes);

/// <summary>
/// <c>research gappers calibrate-spread</c>: the one-time premarket spread multiplier (protocol §Cost
/// model). Takes the first N Tier20 events of the calibration split in (D, symbol) order, the median
/// quoted relative spread within the tolerance of the calibration instant from the quotes endpoint,
/// the Corwin-Schultz estimate over D-1..D-5 from the same feature code the snapshots use, and writes
/// the median ratio to <c>data/spread-multiplier.json</c>. Refuses to run twice without --force, and a
/// forced run leaves an audit line in the runs log.
/// </summary>
public sealed class GapperSpreadCalibrationStage(GapperStudyServices s)
{
    public const string AuditFile = "calibrate-spread-audit.log";

    public Func<StudyKeys, IAlpacaQuoteSource> QuoteSourceFactory { get; set; } = keys => throw new InvalidOperationException("Quote source not configured.");

    public async Task<SpreadCalibrationResult> RunAsync(SpreadCalibrationRequest req, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var p = s.Params;
        var recipe = GapperSpreadMultiplier.RecipeOf(p);
        var split = (req.Split ?? recipe.Split).ToLowerInvariant();
        var events = req.Events ?? recipe.Events;
        if (!string.Equals(split, recipe.Split, StringComparison.OrdinalIgnoreCase) || events != recipe.Events)
            throw new InvalidOperationException($"params.json fixes the calibration at {recipe.Events} {recipe.Split} events; {events} {split} was asked.");
        var file = GapperSpreadMultiplier.File(s.Paths);
        if (File.Exists(file))
        {
            if (!req.Force) throw new InvalidOperationException($"{GapperSpreadMultiplier.FileName} already exists; the multiplier is calibrated once. Pass --force to recalibrate (audited).");
            Directory.CreateDirectory(s.Paths.RunsDir);
            File.AppendAllText(Path.Combine(s.Paths.RunsDir, AuditFile),
                $"{s.UtcNow:O} FORCED recalibration; previous file: {File.ReadAllText(file).Replace('\n', ' ').Replace('\r', ' ')}\n");
        }

        var atEt = GapperStudyParams.ParseEt(recipe.AtEt);
        var tolerance = TimeSpan.FromSeconds(recipe.ToleranceSeconds);
        List<(string Symbol, DateOnly DateEt)> sample;
        await using (var db = await s.Db.CreateDbContextAsync(ct))
        {
            var q = db.GapperStudyEvents.AsNoTracking().Where(e => e.Split == split);
            q = recipe.Tier switch
            {
                "Tier50" => q.Where(e => e.Tier50),
                "Tier100" => q.Where(e => e.Tier100),
                _ => q.Where(e => e.Tier20),
            };
            sample = (await q.OrderBy(e => e.DateEt).ThenBy(e => e.Symbol).Take(events).Select(e => new { e.Symbol, e.DateEt }).ToListAsync(ct))
                .Select(x => (x.Symbol, x.DateEt)).ToList();
        }
        if (sample.Count == 0) throw new InvalidOperationException($"No {recipe.Tier} events in the {split} split; pull bars first.");

        var keys = await s.ResolveKeysAsync(req.Owner, ct);
        var quotes = QuoteSourceFactory(keys);
        var store = await s.StoreAsync(ct);
        var cs = new PriorDaysCorwinSchultzFeature(p.Features.CorwinSchultz.MinBars);
        var ratios = new List<double>();
        int calls = 0, noSpread = 0, noQuotes = 0;
        try
        {
            foreach (var (symbol, day) in sample)
            {
                var priors = MarketCalendar.PriorTradingDays(day, p.Features.CorwinSchultz.PriorDays).Select(d => store.ReadDay(symbol, d)).Where(b => b.Count > 0).ToList();
                var at = MinuteBarMath.ToUtc(day, atEt);
                var inputs = new GapperFeatureInputs { Symbol = symbol, DateEt = day, DecisionUtc = at, MinuteBars = [], PriorDayMinuteBars = priors };
                var spread = cs.Compute(inputs, new GapperFormationSnapshot { Symbol = symbol, DateEt = day, DecisionUtc = at });
                if (spread is not > 0) { noSpread++; continue; }
                calls++;
                var ticks = await quotes.GetQuotesAsync(symbol, at - tolerance, at + tolerance, 10000, ct);
                var quoted = TradingCosts.QuotedSpread(ticks.Select(t => (t.Bid, t.Ask)));
                var rel = ticks.Where(t => t.Bid > 0 && t.Ask >= t.Bid).Select(t => (t.Ask - t.Bid) / ((t.Ask + t.Bid) / 2)).ToList();
                if (rel.Count == 0 || double.IsNaN(quoted)) { noQuotes++; continue; }
                rel.Sort();
                double realized = recipe.Statistic == "mean" ? rel.Average() : rel[rel.Count / 2];
                ratios.Add(realized / spread.Value);
                if (calls % 50 == 0) s.Logger.LogInformation("[{Now}] calibrate-spread: {Calls} events quoted, {Used} usable", GapperStudyServices.Et(s.UtcNow), calls, ratios.Count);
            }
        }
        finally { (quotes as IDisposable)?.Dispose(); }

        double? multiplier = null;
        if (ratios.Count > 0)
        {
            ratios.Sort();
            multiplier = recipe.Statistic == "mean" ? ratios.Average() : ratios[ratios.Count / 2];
        }
        var doc = new JsonObject
        {
            ["multiplier"] = multiplier,
            ["statistic"] = recipe.Statistic,
            ["n"] = ratios.Count,
            ["eventsRequested"] = events,
            ["eventsSampled"] = sample.Count,
            ["eventsWithoutSpreadEstimate"] = noSpread,
            ["eventsWithoutQuotes"] = noQuotes,
            ["split"] = split,
            ["tier"] = recipe.Tier,
            ["atEt"] = recipe.AtEt,
            ["toleranceSeconds"] = recipe.ToleranceSeconds,
            ["order"] = recipe.Order,
            ["quoteSource"] = $"/v2/stocks/{{s}}/quotes feed={p.DataTiers.AlpacaFeed}",
            ["computedUtc"] = s.UtcNow.ToString("O"),
            ["gitHash"] = HoldoutLock.GitFrozenCommit(s.Paths),
            ["head"] = HoldoutLock.GitHead(s.Paths),
            ["hypothesesSha256"] = p.HypothesesSha256,
            ["paramsSha256"] = p.ParamsSha256,
            ["forced"] = req.Force,
        };
        Directory.CreateDirectory(s.Paths.DataDir);
        File.WriteAllText(file, doc.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        return new SpreadCalibrationResult(multiplier, recipe.Statistic, events, sample.Count, ratios.Count, calls, file,
            multiplier is null ? "no usable event (no spread estimate or no quotes)" : $"{noSpread} without a Corwin-Schultz estimate, {noQuotes} without quotes", Math.Round(sw.Elapsed.TotalMinutes, 2));
    }
}
