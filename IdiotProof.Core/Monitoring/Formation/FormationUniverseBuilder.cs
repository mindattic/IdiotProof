using IdiotProof.Core.Catalysts;
using IdiotProof.Core.Data;
using IdiotProof.Core.Services;
using IdiotProof.Evidence.Gappers;
using IdiotProof.Scripting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace IdiotProof.Core.Monitoring.Formation;

public sealed record FormationUniverseResult(IReadOnlyList<FormationMember> Members, string Note);

/// <summary>
/// Gathers the inputs of <see cref="FormationUniverse.Build"/>: <c>TrackedTickers</c> from SQL,
/// catalysts since the prior close through <see cref="CatalystLookup"/>, and Alpaca's movers and
/// most-actives screens with the owner's data keys. A screen that fails leaves a note and the
/// universe is built from the rest.
/// </summary>
public sealed class FormationUniverseBuilder(
    IDbContextFactory<AppDbContext> dbFactory,
    IServiceScopeFactory scopes,
    UserKeyService userKeys,
    ILogger<FormationUniverseBuilder> log)
{
    public const int ScreenTop = 50;

    internal Func<string, string, int, Task<List<(string Symbol, double Percent, double Price)>>> FetchMovers { get; init; } = StrategyScanner.FetchMoversAsync;
    internal Func<string, string, int, Task<List<string>>> FetchMostActives { get; init; } = StrategyScanner.FetchMostActivesAsync;
    /// <summary>Alpaca's tradable-symbol set (null = unavailable, filter nothing). A test seam.</summary>
    internal Func<string, string, CancellationToken, Task<HashSet<string>?>> FetchTradable { get; init; } = AlpacaTradableAssets.GetAsync;

    public async Task<FormationUniverseResult> BuildAsync(Guid owner, DateTime nowUtc, FormationConfig cfg, CancellationToken ct)
    {
        var notes = new List<string>();

        List<TrackedTickerInfo> tracked;
        await using (var db = await dbFactory.CreateDbContextAsync(ct))
        {
            tracked = await db.TrackedTickers.AsNoTracking()
                .Select(t => new TrackedTickerInfo(t.Symbol, t.IsTradable, t.LastPrice, t.SharesOutstanding))
                .ToListAsync(ct);
        }

        var priorClose = PriorCloseUtc(nowUtc);
        List<CatalystSymbolHit> catalysts;
        using (var scope = scopes.CreateScope())
            catalysts = await scope.ServiceProvider.GetRequiredService<CatalystLookup>().RecentSymbolsAsync(priorClose, ct: ct);

        var movers = new List<ScreenerHit>();
        var mostActives = new List<ScreenerHit>();
        var k = await userKeys.GetOrCreateAsync(owner, ct);
        if (string.IsNullOrWhiteSpace(k.AlpacaApiKeyId) || string.IsNullOrWhiteSpace(k.AlpacaApiSecretKey))
        {
            notes.Add("owner has no Alpaca data keys: movers and most-actives skipped");
        }
        else
        {
            try { movers = (await FetchMovers(k.AlpacaApiKeyId, k.AlpacaApiSecretKey, ScreenTop)).Select(m => new ScreenerHit(m.Symbol, m.Percent, m.Price)).ToList(); }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { notes.Add($"movers screen failed: {ex.Message}"); log.LogWarning(ex, "Formation universe: movers screen failed."); }
            try { mostActives = (await FetchMostActives(k.AlpacaApiKeyId, k.AlpacaApiSecretKey, ScreenTop)).Select(s => new ScreenerHit(s)).ToList(); }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { notes.Add($"most-actives screen failed: {ex.Message}"); log.LogWarning(ex, "Formation universe: most-actives screen failed."); }
        }

        // Drop every name Alpaca will not trade before the cap is applied (IP-T495): movers, most-actives and catalyst
        // symbols arrive from screens that list Alpaca-blocked Nasdaq names (OMH, HOLO, ...) too.
        if (!string.IsNullOrWhiteSpace(k.AlpacaApiKeyId) && !string.IsNullOrWhiteSpace(k.AlpacaApiSecretKey))
        {
            var tradable = await FetchTradable(k.AlpacaApiKeyId, k.AlpacaApiSecretKey, ct);
            if (tradable is null) notes.Add("Alpaca tradable list unavailable: not filtered");
            else
            {
                var dropped = new SortedSet<string>(StringComparer.Ordinal);
                bool Keep(string s) { if (tradable.Contains(s.Trim())) return true; dropped.Add(s.Trim().ToUpperInvariant()); return false; }
                tracked = tracked.Where(t => Keep(t.Symbol)).ToList();
                catalysts = catalysts.Where(c => Keep(c.Symbol)).ToList();
                movers = movers.Where(m => Keep(m.Symbol)).ToList();
                mostActives = mostActives.Where(m => Keep(m.Symbol)).ToList();
                notes.Add(dropped.Count == 0 ? "all candidates Alpaca-tradable"
                    : $"{dropped.Count} not tradable on Alpaca dropped ({string.Join(",", dropped.Take(10))}{(dropped.Count > 10 ? ",..." : "")})");
            }
        }

        var members = FormationUniverse.Build(tracked, catalysts, movers, mostActives, cfg);
        notes.Insert(0, $"{tracked.Count} tracked, {catalysts.Select(c => c.Symbol).Distinct().Count()} with catalysts since {MinuteBarMath.ToEastern(priorClose):yyyy-MM-dd HH:mm} ET, {movers.Count} movers, {mostActives.Count} most-active -> {members.Count} (cap {cfg.MaxSymbols})");
        return new FormationUniverseResult(members, string.Join("; ", notes));
    }

    /// <summary>16:00 ET of the last trading day before the session <paramref name="nowUtc"/> belongs to.</summary>
    public static DateTime PriorCloseUtc(DateTime nowUtc)
    {
        var day = MinuteBarMath.SessionDate(nowUtc).AddDays(-1);
        while (day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday || MarketTime.IsMarketHoliday(day)) day = day.AddDays(-1);
        return MinuteBarMath.ToUtc(day, new TimeSpan(16, 0, 0));
    }
}
