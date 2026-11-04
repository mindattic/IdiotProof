using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Text.Json;
using IdiotProof.Core.Data;
using IdiotProof.Core.Evidence;
using IdiotProof.Evidence;
using IdiotProof.Core.Hosting;
using IdiotProof.Engine.Settings;
using IdiotProof.Engine.Storage;
using IdiotProof.Models;
using IdiotProof.Operations;
using IdiotProof.Operations.Evidence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

// IP-T375 research harness: the pre-registered penny-stock walk-forward, run in-process against a
// separate research database (never the live one), read-only market data, no orders.
//   dotnet run --project tools/EvidenceRun -- tune    (grid on 2026-05-01..2026-07-31 only)
//   dotnet run --project tools/EvidenceRun -- eval    (once, frozen params, 2026-08-03..2026-10-30)

const string ResearchDb = "IdiotProof_Research_T375";
var outDir = Environment.GetEnvironmentVariable("EVIDENCE_OUT")
    ?? Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "docs", "research", "penny-walkforward-2026-11"));
Directory.CreateDirectory(outDir);
var mode = args.FirstOrDefault() ?? "tune";

Environment.SetEnvironmentVariable("ConnectionStrings__IdiotProof",
    $@"Server=(localdb)\MSSQLLocalDB;Database={ResearchDb};Trusted_Connection=True;TrustServerCertificate=True;Command Timeout=300;");
Environment.SetEnvironmentVariable(TradingServiceRegistration.FeedEnvVar, "alpaca");

var configuration = new ConfigurationBuilder().AddIdiotProofVault().Build();
var resolved = CoreServiceRegistration.ResolveConnectionString(configuration);
if (!resolved.Contains(ResearchDb, StringComparison.Ordinal))
    throw new InvalidOperationException("Refusing to run: the connection string is not the research database.");

var storage = new WebStorageProvider();
storage.EnsureDirectories();
var services = new ServiceCollection();
services.AddLogging();
services.AddSingleton<IConfiguration>(configuration);
services.AddIdiotProofCore(configuration, storage, new CoreHostOptions { PinDevKeyRing = true });
services.AddIdiotProofTrading();
using var root = services.BuildServiceProvider();

// Migrate with the committed migrations only; uncommitted model edits elsewhere in the shared
// checkout must not block a research run, so the pending-model-changes guard is ignored here.
var migrateOptions = new DbContextOptionsBuilder<AppDbContext>()
    .UseSqlServer(resolved)
    .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning))
    .Options;
await using (var db = new AppDbContext(migrateOptions))
{
    if (!db.Database.GetConnectionString()!.Contains(ResearchDb, StringComparison.Ordinal))
        throw new InvalidOperationException("Refusing to migrate a non-research database.");
    await db.Database.MigrateAsync();
}

var settings = root.GetRequiredService<AppSettings>();
if (string.IsNullOrWhiteSpace(settings.AlpacaApiKeyId)) throw new InvalidOperationException("No Alpaca market-data keys resolved.");
var realStore = root.GetRequiredService<IHistoricalBarStore>();
var candidates = await AlpacaAssets.LoadAsync(settings);
Console.WriteLine($"candidates: {candidates.Count} ({candidates.Count(c => !c.Active)} inactive)");

// Daily bars for every candidate over the whole study span, fetched once through the store.
var store = new PreloadedUniverseStore(realStore, candidates.Select(c => c.Symbol).ToList(),
    new DateOnly(2026, 3, 1), new DateOnly(2026, 10, 30));
await store.PreloadAsync();

var tuneFrom = new DateOnly(2026, 5, 1);
var tuneTo = new DateOnly(2026, 7, 31);
var evalFrom = new DateOnly(2026, 8, 3);
var evalTo = new DateOnly(2026, 10, 30);
const int MaxSymbolsPerDay = 15;
const double Cost = 0.003;

var splits = await AlpacaSplits.LoadAsync(settings, store.DailySymbols(), new DateOnly(2026, 3, 1), evalTo);
Console.WriteLine($"split actions: {(splits.Ok ? $"{splits.ExDates.Sum(kv => kv.Value.Count)} ex-dates over {splits.ExDates.Count} symbols" : "corporate-actions API unavailable (" + splits.Error + "); gap guard only")}");

if (mode == "tune")
{
    // Pre-registered grid: 3 windows x 2 horizons x 2 agreement levels = 12 configurations.
    // Resumable: a configuration already in tune.json (same cached data, same seeds) is not rerun.
    var tunePath = Path.Combine(outDir, "tune.json");
    var results = File.Exists(tunePath)
        ? JsonSerializer.Deserialize<List<TuneRow>>(File.ReadAllText(tunePath), Json.Options)!
        : new List<TuneRow>();
    foreach (var window in new[] { 10, 20, 30 })
    foreach (var horizon in new[] { 5, 15 })
    foreach (var agree in new[] { 0.6, 0.7 })
    {
        if (results.Any(x => x.window == window && x.horizon == horizon && x.agree == agree)) continue;
        var r = await Run(tuneFrom, tuneTo, window, horizon, agree);
        Console.WriteLine($"tune w={window} h={horizon} a={agree}: calls={r.GetProperty("Calls")} hit={r.GetProperty("HitRate")} ciLow={r.GetProperty("ClusteredCiLow")}");
        results.Add(new TuneRow(window, horizon, agree, r));
        File.WriteAllText(tunePath, JsonSerializer.Serialize(results, Json.Options));
    }
    // Pre-registered selection: highest day-clustered bootstrap lower bound among configs with >= 200 calls.
    var best = results
        .Where(t => t.result.GetProperty("Calls").GetInt32() >= 200)
        .OrderByDescending(t => Json.Num(t.result.GetProperty("ClusteredCiLow")))
        .FirstOrDefault();
    if (best is null) { Console.WriteLine("No configuration reached 200 calls; nothing to freeze."); return; }
    var frozen = new { best.window, best.horizon, best.agree };
    File.WriteAllText(Path.Combine(outDir, "frozen.json"), JsonSerializer.Serialize(frozen, Json.Options));
    Console.WriteLine($"frozen: {JsonSerializer.Serialize(frozen)}");
}
else if (mode == "eval")
{
    var evalPath = Path.Combine(outDir, "eval.json");
    if (File.Exists(evalPath)) throw new InvalidOperationException("The evaluation already ran once; the protocol forbids a rerun.");
    var frozen = JsonDocument.Parse(File.ReadAllText(Path.Combine(outDir, "frozen.json"))).RootElement;
    var r = await Run(evalFrom, evalTo, frozen.GetProperty("window").GetInt32(), frozen.GetProperty("horizon").GetInt32(), frozen.GetProperty("agree").GetDouble(), evalPath);
    Console.WriteLine(JsonSerializer.Serialize(r, Json.Options));
}

async Task<JsonElement> Run(DateOnly from, DateOnly to, int window, int horizon, double agree, string? jsonPath = null)
{
    // Same plan as `evidence walkforward --universe penny`: each weekday's point-in-time members, top N by dollar volume.
    var plan = new Dictionary<string, SortedSet<DateOnly>>(StringComparer.Ordinal);
    int universeDays = 0, memberDays = 0;
    for (var d = from; d <= to; d = d.AddDays(1))
    {
        if (d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) continue;
        var members = await store.GetPointInTimeUniverseAsync(d, candidates.Select(c => c.Symbol));
        if (members.Count == 0) continue;
        universeDays++;
        memberDays += members.Count;
        foreach (var m in members.OrderByDescending(m => m.AvgDollarVolume).Take(MaxSymbolsPerDay))
        {
            if (!plan.TryGetValue(m.Symbol, out var set)) plan[m.Symbol] = set = new SortedSet<DateOnly>();
            set.Add(d);
        }
    }
    var wf = new NextOpenWalkForward(store, store.Daily, splits.ExDates);
    var r = await wf.RunAsync(plan, new NextOpenWalkForward.Settings(window, horizon, agree));
    var summary = Report.Summarize(r, Cost, from, to, window, horizon, agree, universeDays, memberDays, plan.Count,
        candidates.Count, candidates.Count(c => !c.Active), store.Rejected, splits.Ok);
    var json = JsonSerializer.SerializeToElement(summary, Json.Options);
    if (jsonPath is not null) File.WriteAllText(jsonPath, JsonSerializer.Serialize(json, Json.Options));
    return json;
}

static class AlpacaAssets
{
    /// <summary>Every US equity Alpaca knows, active and inactive (no status filter = all statuses).</summary>
    public static async Task<IReadOnlyList<CandidateAsset>> LoadAsync(AppSettings s)
    {
        using var http = new HttpClient();
        http.DefaultRequestHeaders.Add("APCA-API-KEY-ID", s.AlpacaApiKeyId);
        http.DefaultRequestHeaders.Add("APCA-API-SECRET-KEY", s.AlpacaApiSecretKey);
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        var host = s.AlpacaIsPaper ? "https://paper-api.alpaca.markets" : "https://api.alpaca.markets";
        var list = new List<CandidateAsset>();
        foreach (var status in new[] { "active", "inactive" })
        {
            var json = await http.GetStringAsync($"{host}/v2/assets?asset_class=us_equity&status={status}");
            foreach (var a in JsonDocument.Parse(json).RootElement.EnumerateArray())
            {
                var sym = (a.GetProperty("symbol").GetString() ?? "").Trim().ToUpperInvariant();
                if (sym.Length == 0 || sym.Contains('.') || sym.Contains('/')) continue;
                list.Add(new CandidateAsset(sym, status == "active"));
            }
        }
        return list.DistinctBy(c => c.Symbol).ToList();
    }
}

/// <summary>
/// Minute bars go straight through the SQL-cached store. Daily bars for every candidate are loaded
/// once for the study span, and the point-in-time universe is computed from them in memory with the
/// store's own rule (UniverseFilter defaults): only daily bars dated before the as-of day.
/// </summary>
sealed class PreloadedUniverseStore(IHistoricalBarStore inner, IReadOnlyList<string> symbols, DateOnly from, DateOnly to) : IHistoricalBarStore
{
    private readonly ConcurrentDictionary<string, IReadOnlyList<Candle>> daily = new(StringComparer.Ordinal);

    public async Task PreloadAsync()
    {
        int done = 0;
        using var gate = new SemaphoreSlim(8);
        foreach (var chunk in symbols.Chunk(200))
        {
            await Task.WhenAll(chunk.Select(async s =>
            {
                await gate.WaitAsync();
                try { daily[s] = await inner.GetBarsAsync(s, from, to, BarTimeframe.Day1); }
                catch (HttpRequestException e) when (e.StatusCode is System.Net.HttpStatusCode.BadRequest or System.Net.HttpStatusCode.NotFound)
                {
                    // Placeholder tickers among inactive assets that the data API refuses: no bars, never in the universe.
                    Interlocked.Increment(ref rejected);
                }
                finally { gate.Release(); }
            }));
            done += chunk.Length;
            Console.WriteLine($"daily preload {done}/{symbols.Count} (rejected {rejected})");
        }
    }

    private int rejected;
    public int Rejected => rejected;

    public IReadOnlyDictionary<string, IReadOnlyList<Candle>> Daily => daily;

    /// <summary>Symbols with any daily bars in the study span.</summary>
    public IReadOnlyList<string> DailySymbols() => daily.Where(kv => kv.Value.Count > 0).Select(kv => kv.Key).OrderBy(s => s, StringComparer.Ordinal).ToList();

    public Task<IReadOnlyList<Candle>> GetBarsAsync(string symbol, DateOnly fromEt, DateOnly toEt, BarTimeframe timeframe, CancellationToken ct = default) =>
        inner.GetBarsAsync(symbol, fromEt, toEt, timeframe, ct);

    public Task<IReadOnlyDictionary<string, IReadOnlyList<Candle>>> GetBarsForSymbolsAsync(IEnumerable<string> syms, DateOnly fromEt, DateOnly toEt,
        BarTimeframe timeframe, int maxConcurrency = 4, CancellationToken ct = default) =>
        inner.GetBarsForSymbolsAsync(syms, fromEt, toEt, timeframe, 8, ct);

    public Task<IReadOnlyList<UniverseMember>> GetPointInTimeUniverseAsync(DateOnly asOfEt, IEnumerable<string> candidates, UniverseFilter? filter = null, CancellationToken ct = default)
    {
        filter ??= new UniverseFilter();
        var members = new List<UniverseMember>();
        foreach (var symbol in candidates)
        {
            if (!daily.TryGetValue(symbol, out var all)) continue;
            var window = all.Where(c => DateOf(c) < asOfEt).OrderBy(c => c.StartUtc).TakeLast(filter.LookbackSessions).ToList();
            if (window.Count < Math.Min(filter.MinSessions, filter.LookbackSessions)) continue;
            var last = window[^1];
            if (last.Close >= filter.MaxPrice || last.Close < filter.MinPrice) continue;
            var adv = window.Average(c => c.Close * c.Volume);
            if (adv < filter.MinAvgDollarVolume) continue;
            members.Add(new UniverseMember(symbol, DateOf(last), last.Close, decimal.Round(adv, 2), window.Count));
        }
        return Task.FromResult<IReadOnlyList<UniverseMember>>(members.OrderBy(m => m.Symbol, StringComparer.Ordinal).ToList());
    }

    // Same rule as HistoricalBarStore.DateOf for Day1 bars (stamped at ET midnight).
    private static DateOnly DateOf(Candle c) => DateOnly.FromDateTime(c.StartUtc);
}

static class Json
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals,
    };

    /// <summary>A number that may have been written as "NaN"/"Infinity"; NaN sorts last.</summary>
    public static double Num(JsonElement e) => e.ValueKind == JsonValueKind.Number
        ? e.GetDouble()
        : double.TryParse(e.GetString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v) && !double.IsNaN(v) ? v : double.NegativeInfinity;
}

sealed record TuneRow(int window, int horizon, double agree, JsonElement result);

/// <summary>Split ex-dates from Alpaca's corporate-actions API (forward and reverse splits).</summary>
static class AlpacaSplits
{
    public sealed record Splits(bool Ok, string? Error, Dictionary<string, List<DateOnly>> ExDates);

    public static async Task<Splits> LoadAsync(AppSettings s, IReadOnlyList<string> symbols, DateOnly from, DateOnly to)
    {
        var map = new Dictionary<string, List<DateOnly>>(StringComparer.Ordinal);
        using var http = new HttpClient();
        http.DefaultRequestHeaders.Add("APCA-API-KEY-ID", s.AlpacaApiKeyId);
        http.DefaultRequestHeaders.Add("APCA-API-SECRET-KEY", s.AlpacaApiSecretKey);
        try
        {
            foreach (var chunk in symbols.Chunk(100))
            {
                string? token = null;
                do
                {
                    var url = $"https://data.alpaca.markets/v1/corporate-actions?symbols={string.Join(',', chunk)}&types=forward_split,reverse_split&start={from:yyyy-MM-dd}&end={to:yyyy-MM-dd}&limit=1000"
                              + (token is null ? "" : $"&page_token={token}");
                    using var resp = await http.GetAsync(url);
                    var body = await resp.Content.ReadAsStringAsync();
                    if (!resp.IsSuccessStatusCode) return new Splits(false, $"{(int)resp.StatusCode}: {body[..Math.Min(120, body.Length)]}", map);
                    var root = JsonDocument.Parse(body).RootElement;
                    if (root.TryGetProperty("corporate_actions", out var ca))
                        foreach (var kind in new[] { "forward_splits", "reverse_splits" })
                            if (ca.TryGetProperty(kind, out var arr) && arr.ValueKind == JsonValueKind.Array)
                                foreach (var a in arr.EnumerateArray())
                                {
                                    var sym = a.GetProperty("symbol").GetString()!.ToUpperInvariant();
                                    if (!DateOnly.TryParse(a.GetProperty("ex_date").GetString(), out var ex)) continue;
                                    if (!map.TryGetValue(sym, out var list)) map[sym] = list = new List<DateOnly>();
                                    list.Add(ex);
                                }
                    token = root.TryGetProperty("next_page_token", out var nt) && nt.ValueKind == JsonValueKind.String ? nt.GetString() : null;
                } while (!string.IsNullOrEmpty(token));
            }
            return new Splits(true, null, map);
        }
        catch (Exception e) { return new Splits(false, e.Message, map); }
    }
}

static class Report
{
    public static object Summarize(NextOpenWalkForward.Result r, double cost, DateOnly from, DateOnly to, int window, int horizon, double agree,
        int universeDays, int memberDays, int planSymbols, int candidates, int inactive, int rejected, bool splitActionsOk)
    {
        var o = r.NextOpen;
        var (lo, hi) = o.HitRateInterval;
        var c50 = ClusteredSignificance.Evaluate(r.NextOpenDays, 0.5);
        var c51 = ClusteredSignificance.Evaluate(r.NextOpenDays, 0.51);
        var cc = r.CloseEntry;
        var cc50 = ClusteredSignificance.Evaluate(r.CloseDays, 0.5);
        int decided = r.Ups + r.Downs;
        double upBase = decided == 0 ? double.NaN : (double)r.Ups / decided;
        var months = r.NextOpenDays.GroupBy(t => $"{t.Day:yyyy-MM}").OrderBy(g => g.Key).Select(g =>
        {
            int calls = g.Sum(t => t.Calls), hits = g.Sum(t => t.Hits);
            return new { Month = g.Key, Days = g.Count(t => t.Calls > 0), Calls = calls, Hits = hits, HitRate = calls == 0 ? double.NaN : (double)hits / calls };
        }).ToList();
        return new
        {
            Entry = "next-open (bar i+1 open -> bar i+h close)", From = from, To = to, Window = window, Horizon = horizon, MinAgreement = agree,
            TrainDays = 20, K = 25, MinSimilarity = 0.8, MinNeighbors = 8, Cost = cost,
            Candidates = candidates, InactiveCandidates = inactive, RejectedTickers = rejected,
            UniverseDays = universeDays, AvgUniverseSize = universeDays == 0 ? 0 : (double)memberDays / universeDays, PlanSymbols = planSymbols,
            SymbolDays = r.SymbolDays, SkippedSymbolDays = r.Skipped,
            SplitExcludedByCorporateAction = r.SplitExcludedByAction, SplitExcludedByGap = r.SplitExcludedByGap, SplitActionsAvailable = splitActionsOk,
            EvaluationPoints = r.Points, o.Calls, o.Hits, HitRate = o.HitRate, CiLow = lo, CiHigh = hi, NaiveP = o.PValue,
            Coverage = o.Coverage, UpBaseRate = upBase, BestConstantGuess = double.IsNaN(upBase) ? double.NaN : Math.Max(upBase, 1 - upBase),
            MeanEdge = o.MeanEdge, MeanEdgeAfterCost = o.MeanEdgeAfterCost(cost),
            ClusteredDays = c50.Days, DayMeanHitRate = c50.DayMeanHitRate, ClusteredBootstrapP = c50.BootstrapPValue, ClusteredTP = c50.TPValue,
            ClusteredCiLow = c50.BootstrapLow, ClusteredCiHigh = c50.BootstrapHigh,
            Clustered51BootstrapP = c51.BootstrapPValue, Clustered51TP = c51.TPValue,
            Months = months,
            TiesExcludedDiagnostic = new
            {
                Note = "Calls whose next-open outcome was exactly 0 (common at 1-cent ticks) removed; diagnostic only, verdict uses the strict scoring above.",
                Ties = r.Ties, r.NextOpenNoTies.Calls, r.NextOpenNoTies.Hits, HitRate = r.NextOpenNoTies.HitRate,
                ClusteredBootstrapP = ClusteredSignificance.Evaluate(r.NoTieDays, 0.5).BootstrapPValue,
                ClusteredTP = ClusteredSignificance.Evaluate(r.NoTieDays, 0.5).TPValue,
                Clustered51BootstrapP = ClusteredSignificance.Evaluate(r.NoTieDays, 0.51).BootstrapPValue,
                Clustered51TP = ClusteredSignificance.Evaluate(r.NoTieDays, 0.51).TPValue,
            },
            CloseEntryDiagnostic = new
            {
                Note = "Same calls scored at the signal bar's close; diagnostic only, never used for selection.",
                cc.Calls, cc.Hits, HitRate = cc.HitRate, NaiveP = cc.PValue, ClusteredBootstrapP = cc50.BootstrapPValue, ClusteredTP = cc50.TPValue,
                MeanEdge = cc.MeanEdge,
            },
        };
    }
}
