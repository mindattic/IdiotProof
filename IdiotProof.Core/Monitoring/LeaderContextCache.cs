using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using IdiotProof.Core.Evidence;
using IdiotProof.DataFeeds;
using IdiotProof.Evidence.Leaders;
using IdiotProof.Models;
using IdiotProof.Scripting;
using IdiotProof.Shared;
using Microsoft.Extensions.Logging;

namespace IdiotProof.Core.Monitoring;

/// <summary>
/// Fills <see cref="IndicatorSnapshot.Leaders"/> for LeaderConfirms / NotLeaderDiverging (IP-T391).
/// Once per symbol per ET day it discovers the symbol's lag-1 leaders among the configured ETFs and
/// the other armed symbols, from 1-minute bars of the prior sessions only (the bar store when one is
/// registered, else the feed), so today's tape never chooses a leader. Each tick it reads every
/// leader's move over its last closed bars from the feed. Any failure leaves the snapshot's
/// Leaders null, which blocks both guards.
/// </summary>
public sealed class LeaderContextCache(IMarketDataFeed feed, IHistoricalBarStore? barStore, Func<string> etfs, ILogger logger)
{
    public const int DiscoverySessions = 10;
    public const int MaxHops = 3;
    public const int LookbackBars = 5;
    private const int MaxCandidates = 15;
    private static readonly TimeSpan MoveRefresh = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan BarSize = TimeSpan.FromMinutes(1);

    private sealed record Discovery(DateOnly Day, IReadOnlyList<LeaderLink> Links);
    private sealed record Moves(DateTime FetchedUtc, IReadOnlyList<Candle> Bars);

    private readonly ConcurrentDictionary<string, Discovery> discoveries = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, Moves> recent = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, DateTime> retryAfter = new(StringComparer.OrdinalIgnoreCase);
    private static readonly TimeSpan FailureBackoff = TimeSpan.FromMinutes(5);

    public async Task ApplyAsync(string symbol, IndicatorSnapshot snapshot, IEnumerable<string> armedSymbols, DateTime nowUtc, CancellationToken ct)
    {
        if (retryAfter.TryGetValue(symbol, out var until) && nowUtc < until) { snapshot.Leaders = null; return; }
        try
        {
            var links = await LeadersAsync(symbol, armedSymbols, nowUtc, ct);
            var readings = new List<LeaderReading>(links.Count);
            foreach (var link in links)
            {
                var bars = await RecentBarsAsync(link.Symbol, nowUtc, ct);
                readings.Add(new LeaderReading(link.Symbol, link.Hops, link.Influence, LeaderSignal.RecentMove(bars, nowUtc, LookbackBars)));
            }
            snapshot.Leaders = readings;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Leader context for {Symbol} failed; leader guards block for {Backoff}.", symbol, FailureBackoff);
            retryAfter[symbol] = nowUtc + FailureBackoff;
            snapshot.Leaders = null;
        }
    }

    private static readonly ConditionalWeakTable<StrategyDefinition, object> usesLeaders = new();

    /// <summary>Whether any entry condition, While guard or branch of <paramref name="def"/> reads the leaders.</summary>
    public static bool Uses(StrategyDefinition def) =>
        (bool)usesLeaders.GetValue(def, d =>
            d.EntryConditions.Concat(d.WhileConditions).Any(c => Mentions(c.ToScript()))
            || d.ConditionalBlocks.Any(b => Mentions(b.ToScript())));

    private static bool Mentions(string script) =>
        script.Contains("LeaderConfirms(", StringComparison.Ordinal) || script.Contains("NotLeaderDiverging(", StringComparison.Ordinal);

    /// <summary>The ETFs plus the other armed symbols, deduplicated, without the target itself.</summary>
    public static IReadOnlyList<string> Candidates(string symbol, string etfList, IEnumerable<string> armedSymbols) =>
        etfList.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Concat(armedSymbols)
            .Select(s => s.Trim().ToUpperInvariant())
            .Where(s => s.Length > 0 && !s.Equals(symbol, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.Ordinal)
            .Take(MaxCandidates)
            .ToList();

    private async Task<IReadOnlyList<LeaderLink>> LeadersAsync(string symbol, IEnumerable<string> armedSymbols, DateTime nowUtc, CancellationToken ct)
    {
        var day = EtDay(nowUtc);
        if (discoveries.TryGetValue(symbol, out var cached) && cached.Day == day) return cached.Links;

        var candidates = Candidates(symbol, etfs(), armedSymbols);
        var history = await HistoryAsync(candidates.Prepend(symbol.ToUpperInvariant()).ToList(), day, ct);
        var links = LeaderDiscovery.Discover(symbol.ToUpperInvariant(), history, MaxHops);
        discoveries[symbol] = new Discovery(day, links);
        logger.LogInformation("Leaders for {Symbol} on {Day}: {Leaders}", symbol, day,
            links.Count == 0 ? "(none measured)" : string.Join(", ", links.Select(l => $"{l.Symbol} {l.Hops}h {l.Influence:+0.00;-0.00}")));
        return links;
    }

    /// <summary>1-minute bars of the sessions strictly before <paramref name="day"/>.</summary>
    private async Task<IReadOnlyDictionary<string, IReadOnlyList<Candle>>> HistoryAsync(IReadOnlyList<string> symbols, DateOnly day, CancellationToken ct)
    {
        var from = day.AddDays(-(DiscoverySessions * 3 / 2 + 4));
        var to = day.AddDays(-1);
        if (barStore is not null)
            return await barStore.GetBarsForSymbolsAsync(symbols, from, to, BarTimeframe.Minute1, ct: ct);

        var dayStartUtc = TimeZoneInfo.ConvertTimeToUtc(day.ToDateTime(TimeOnly.MinValue), MarketTime.Eastern);
        var fromUtc = TimeZoneInfo.ConvertTimeToUtc(from.ToDateTime(TimeOnly.MinValue), MarketTime.Eastern);
        var result = new Dictionary<string, IReadOnlyList<Candle>>(StringComparer.Ordinal);
        foreach (var s in symbols)
        {
            var bars = new List<Candle>();
            await foreach (var c in feed.GetHistoricalCandlesAsync(s, fromUtc, dayStartUtc, BarSize, ct))
                if (c.EndUtc <= dayStartUtc) bars.Add(c);
            result[s] = bars;
        }
        return result;
    }

    private async Task<IReadOnlyList<Candle>> RecentBarsAsync(string symbol, DateTime nowUtc, CancellationToken ct)
    {
        if (recent.TryGetValue(symbol, out var cached) && nowUtc - cached.FetchedUtc < MoveRefresh) return cached.Bars;
        var bars = new List<Candle>();
        await foreach (var c in feed.GetHistoricalCandlesAsync(symbol, nowUtc.AddMinutes(-(LookbackBars * 6)), nowUtc, BarSize, ct))
            bars.Add(c);
        recent[symbol] = new Moves(nowUtc, bars);
        return bars;
    }

    private static DateOnly EtDay(DateTime utc) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), MarketTime.Eastern));
}
