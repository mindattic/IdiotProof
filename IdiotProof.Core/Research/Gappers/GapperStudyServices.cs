using System.Net.Http.Headers;
using System.Text.Json;
using IdiotProof.Core.Data;
using IdiotProof.Core.Evidence;
using IdiotProof.Core.Hosting;
using IdiotProof.Core.Services;
using IdiotProof.DataFeeds;
using IdiotProof.Engine.Settings;
using Microsoft.EntityFrameworkCore;

namespace IdiotProof.Core.Research.Gappers;

/// <summary>Which Alpaca key pair a stage pulls with and where it came from.</summary>
public sealed record StudyKeys(string KeyId, string Secret, string Source);

/// <summary>One Alpaca us_equity asset as the assets endpoint lists it.</summary>
public sealed record StudyAsset(string Symbol, string Name, bool Active);

/// <summary>
/// Paces the Alpaca data API at the frozen requests-per-minute: one process-wide gate, so the
/// multi-symbol bars loader and the split source never exceed it together.
/// </summary>
public sealed class AlpacaDataRateGate(int requestsPerMinute, TimeProvider? time = null)
{
    private readonly TimeProvider time = time ?? TimeProvider.System;
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly TimeSpan spacing = TimeSpan.FromMilliseconds(60_000.0 / Math.Max(1, requestsPerMinute));
    private long lastTicks;

    public long Requests { get; private set; }

    public async Task WaitAsync(CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            var now = time.GetTimestamp();
            var since = time.GetElapsedTime(lastTicks, now);
            if (lastTicks != 0 && since < spacing) await Task.Delay(spacing - since, time, ct);
            lastTicks = time.GetTimestamp();
            Requests++;
        }
        finally { gate.Release(); }
    }
}

/// <summary>
/// What every gapper-study stage shares (IP-T442): the frozen params and paths, the research-DB
/// guard, the Alpaca keys (host settings first, else the owner's), the file-backed minute store
/// rooted at <c>gapperStudy.barRoot</c> (default from params), one data-API rate gate, and the
/// resumable coverage marks.
/// </summary>
public sealed class GapperStudyServices(
    IDbContextFactory<AppDbContext> dbFactory,
    IdiotProofDatabase database,
    AppSettings settings,
    UserKeyService userKeys,
    SettingsRepository settingsRepo,
    TimeProvider clock,
    ILogger<GapperStudyServices> logger)
{
    public const string BarRootSettingKey = "gapperStudy.barRoot";
    public const string SmokeSplit = "smoke";
    public const string NoSplit = "none";

    private static readonly object gateLock = new();
    private static AlpacaDataRateGate? sharedGate;

    private GapperStudyPaths? paths;
    private GapperStudyParams? parameters;

    public IDbContextFactory<AppDbContext> Db => dbFactory;
    public TimeProvider Clock => clock;
    public ILogger Logger => logger;
    public DateTime UtcNow => clock.GetUtcNow().UtcDateTime;

    public GapperStudyPaths Paths => paths ??= GapperStudyPaths.Resolve();
    public GapperStudyParams Params => parameters ??= GapperStudyParams.Load(Paths);

    /// <summary>Null when the connection string names a research database; otherwise why the stage must refuse.</summary>
    public string? GuardError()
    {
        var pattern = Params.DataTiers.ResearchDbGuardPattern;
        return database.ConnectionString.Contains(pattern, StringComparison.Ordinal)
            ? null
            : $"Refusing to run: the connection string is not a research database (its database name must contain '{pattern}').";
    }

    public AlpacaDataRateGate RateGate
    {
        get
        {
            lock (gateLock) return sharedGate ??= new AlpacaDataRateGate(Params.DataTiers.AlpacaRequestsPerMinute, clock);
        }
    }

    public async Task<StudyKeys> ResolveKeysAsync(Guid? owner, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(settings.AlpacaApiKeyId) && !string.IsNullOrWhiteSpace(settings.AlpacaApiSecretKey))
            return new StudyKeys(settings.AlpacaApiKeyId, settings.AlpacaApiSecretKey, "host AppSettings");
        if (owner is { } o)
        {
            var k = await userKeys.GetOrCreateAsync(o, ct);
            if (!string.IsNullOrWhiteSpace(k.AlpacaApiKeyId) && !string.IsNullOrWhiteSpace(k.AlpacaApiSecretKey))
                return new StudyKeys(k.AlpacaApiKeyId, k.AlpacaApiSecretKey, $"owner {o}");
        }
        throw new InvalidOperationException("No Alpaca keys: the host has none in AppSettings and no owner with keys was named (--user).");
    }

    public AlpacaDataFeed Feed(StudyKeys keys) => new(keys.KeyId, keys.Secret, Params.DataTiers.AlpacaFeed);

    public AlpacaSplitSource Splits(StudyKeys keys) => new(keys.KeyId, keys.Secret);

    public async Task<string> BarRootAsync(CancellationToken ct)
    {
        var configured = Environment.GetEnvironmentVariable("GAPPER_STUDY_BAR_ROOT");
        if (string.IsNullOrWhiteSpace(configured)) configured = await settingsRepo.GetAsync(BarRootSettingKey, ct);
        return string.IsNullOrWhiteSpace(configured) ? Params.DataTiers.MinuteBarRoot() : configured.Trim();
    }

    public async Task<FileBackedBarStore> StoreAsync(CancellationToken ct) => new(await BarRootAsync(ct), clock: clock);

    /// <summary>Every us_equity asset, active and inactive, through the trading API (assets are not on the data host).</summary>
    public async Task<List<StudyAsset>> LoadAssetsAsync(StudyKeys keys, CancellationToken ct)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
        http.DefaultRequestHeaders.Add("APCA-API-KEY-ID", keys.KeyId);
        http.DefaultRequestHeaders.Add("APCA-API-SECRET-KEY", keys.Secret);
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        var host = settings.AlpacaIsPaper ? "https://paper-api.alpaca.markets" : "https://api.alpaca.markets";
        var list = new List<StudyAsset>();
        foreach (var status in Params.Universe.AssetStatus)
        {
            await RateGate.WaitAsync(ct);
            var json = await http.GetStringAsync($"{host}/v2/assets?asset_class={Params.Universe.AssetClass}&status={status}", ct);
            foreach (var a in JsonDocument.Parse(json).RootElement.EnumerateArray())
            {
                var sym = (a.GetProperty("symbol").GetString() ?? "").Trim().ToUpperInvariant();
                if (sym.Length == 0 || sym.Length > 16 || sym.Contains('.') || sym.Contains('/')) continue;
                var name = a.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString() ?? "" : "";
                list.Add(new StudyAsset(sym, name.Length > 200 ? name[..200] : name, status == "active"));
            }
        }
        return list.GroupBy(a => a.Symbol).Select(g => g.OrderByDescending(a => a.Active).First()).OrderBy(a => a.Symbol, StringComparer.Ordinal).ToList();
    }

    // ── Coverage marks ───────────────────────────────────────────────────

    public async Task<HashSet<string>> CoveredAsync(string tier, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return (await db.GapperStudyCoverage.AsNoTracking().Where(c => c.Tier == tier).Select(c => c.Key).ToListAsync(ct)).ToHashSet(StringComparer.Ordinal);
    }

    public async Task MarkAsync(string tier, IEnumerable<(string Key, int Count)> keys, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var now = UtcNow;
        var list = keys.ToList();
        var existing = (await db.GapperStudyCoverage.Where(c => c.Tier == tier && list.Select(k => k.Key).Contains(c.Key)).Select(c => c.Key).ToListAsync(ct)).ToHashSet();
        foreach (var (key, count) in list)
            if (!existing.Contains(key)) db.GapperStudyCoverage.Add(new GapperStudyCoverage { Tier = tier, Key = key, Count = count, CompletedUtc = now });
        await db.SaveChangesAsync(ct);
    }

    public string SplitOf(DateOnly d, bool smoke) => smoke ? SmokeSplit : Params.SplitOf(d) ?? NoSplit;

    /// <summary>Bulk delete that also works on the in-memory provider the tests use.</summary>
    public static async Task<int> DeleteAsync<T>(AppDbContext db, IQueryable<T> rows, CancellationToken ct) where T : class
    {
        if (db.Database.ProviderName?.Contains("InMemory", StringComparison.OrdinalIgnoreCase) == true)
        {
            var list = await rows.ToListAsync(ct);
            db.RemoveRange(list);
            await db.SaveChangesAsync(ct);
            return list.Count;
        }
        return await rows.ExecuteDeleteAsync(ct);
    }

    public static string Et(DateTime utc) => EasternTime.FromUtc(utc).ToString("yyyy-MM-dd HH:mm:ss") + " ET";
}
