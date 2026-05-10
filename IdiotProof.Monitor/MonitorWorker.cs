using IdiotProof.Blazor.Services;
using IdiotProof.DataFeeds;
using IdiotProof.Models;
using IdiotProof.Scripting;
using IdiotProof.Strategies;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace IdiotProof.Monitor;

/// <summary>
/// Long-running evaluation loop. Wakes on a fixed cadence (default 30s),
/// loads every active strategy, evaluates each, and logs per-condition
/// progress. Replaces the Blazor-hosted StrategyExecutionService when the
/// Monitor runs standalone — useful for after-hours / overnight evaluation
/// when you don't want a web server up.
///
/// Per-condition progress (the user's "each condition that passes pushes it
/// to the next condition" requirement) is logged at Info level: every
/// evaluation pass emits one line per strategy showing N-of-M conditions
/// passing, with a summary tail of the first failing condition's script form.
///
/// Future iteration: push progress to a TradeSignals / ConditionProgress SQL
/// table so the Strategies page can render "currently 3/5 conditions met"
/// status badges. For now, stdout is the source of truth — pipe to a log file
/// for inspection.
/// </summary>
public sealed class MonitorWorker(
    StrategyRepository strategyRepo,
    ConditionProgressRepository progressRepo,
    ILogger<MonitorWorker> logger) : BackgroundService
{
    /// <summary>Interval between full evaluation passes. Override via env var.</summary>
    private static readonly TimeSpan EvaluationInterval =
        TryParseInterval(Environment.GetEnvironmentVariable("IDIOTPROOF_MONITOR_INTERVAL"))
        ?? TimeSpan.FromSeconds(30);

    /// <summary>How many candles to fetch per ticker per tick.</summary>
    private const int CandleWindow = 120;

    /// <summary>Mock candles cadence — 5-minute bars.</summary>
    private static readonly TimeSpan BarSize = TimeSpan.FromMinutes(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("IdiotProof.Monitor starting — evaluation interval {Interval}s", EvaluationInterval.TotalSeconds);

        // Mock data feed for now — real installs would inject PolygonDataFeed
        // (or any IMarketDataFeed) by reading the same AppSettings the Blazor host
        // does. Swappable: assign a different IMarketDataFeed to `feed`.
        IMarketDataFeed feed = new MockDataFeed();

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await TickAsync(feed, stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Monitor tick threw — continuing on next interval.");
            }

            try { await Task.Delay(EvaluationInterval, stoppingToken); }
            catch (TaskCanceledException) { /* graceful shutdown */ }
        }

        logger.LogInformation("IdiotProof.Monitor stopped.");
    }

    /// <summary>One full evaluation pass — load active strategies, group by symbol, evaluate.</summary>
    private async Task TickAsync(IMarketDataFeed feed, CancellationToken ct)
    {
        var active = await strategyRepo.GetActiveAsync(ct);
        if (active.Count == 0)
        {
            logger.LogDebug("No active strategies — sleeping.");
            return;
        }

        // Group by symbol so we fetch candles once per ticker rather than once per strategy.
        var bySymbol = active.GroupBy(s => s.Symbol, StringComparer.OrdinalIgnoreCase);

        foreach (var group in bySymbol)
        {
            var symbol = group.Key.ToUpperInvariant();
            IReadOnlyList<Candle> candles;
            try
            {
                candles = await FetchCandlesAsync(feed, symbol, ct);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Could not fetch candles for {Symbol}; skipping its strategies this tick.", symbol);
                continue;
            }

            if (candles.Count == 0)
            {
                logger.LogDebug("Empty candle window for {Symbol}; skipping.", symbol);
                continue;
            }

            foreach (var s in group)
            {
                await EvaluateOneAsync(s, candles, ct);
            }
        }
    }

    /// <summary>Fetch the candle history window for a symbol from the configured feed.</summary>
    private static async Task<IReadOnlyList<Candle>> FetchCandlesAsync(IMarketDataFeed feed, string symbol, CancellationToken ct)
    {
        var endUtc = DateTime.UtcNow;
        var startUtc = endUtc - (CandleWindow * BarSize);
        var list = new List<Candle>();
        await foreach (var c in feed.GetHistoricalCandlesAsync(symbol, startUtc, endUtc, BarSize, ct))
        {
            list.Add(c);
        }
        return list;
    }

    /// <summary>Evaluate a single strategy + log per-condition progress.</summary>
    private async Task EvaluateOneAsync(IdiotProof.Blazor.Data.Strategy stored, IReadOnlyList<Candle> candles, CancellationToken ct)
    {
        // Parse the stored ScriptText into a StrategyDefinition. Skip strategies
        // that don't parse — the Strategies-as-home page surfaces parse errors.
        var def = WikilinkParser.ParseScript(stored.ScriptText);
        if (def is null)
        {
            logger.LogWarning("Strategy {Title} ({Id}) failed to parse — skipping.", stored.Title, stored.Id);
            return;
        }

        // Build the snapshot once + walk each condition individually so we can
        // log progress. The DslStrategy adapter normally short-circuits on first
        // failure; here we evaluate each independently for visibility, then
        // hand-roll a TradeSignal when ALL pass.
        var emas = CollectEmaPeriods(def);
        var snapshot = IndicatorSnapshotBuilder.BuildWithEmas(stored.Symbol, candles, emas);

        var conditions = def.EntryConditions;
        if (conditions.Count == 0)
        {
            // No entry conditions — pure setup-only strategy. Treat as fired.
            logger.LogInformation("[{Title}] no entry conditions — auto-fire.", stored.Title);
            await strategyRepo.RecordFiredAsync(stored.Id, ct);
            await progressRepo.UpsertAsync(stored.Id, 0, 0, null, ct);
            return;
        }

        int passed = 0;
        string? firstFailure = null;
        foreach (var cond in conditions)
        {
            if (cond.Evaluate(snapshot))
            {
                passed++;
            }
            else
            {
                firstFailure = cond.ToScript();
                break; // sequential progress: stop on first fail
            }
        }

        var total = conditions.Count;

        // Persist progress so the Strategies page can render a live badge
        // ("3/5 — waiting on OnReclaim(9)") without tailing stdout.
        await progressRepo.UpsertAsync(stored.Id, passed, total, firstFailure, ct);

        if (passed == total)
        {
            logger.LogInformation("[{Title}] {Symbol} ✓ ALL {Passed}/{Total} conditions met → SIGNAL ({Direction} @ {Price:F2})",
                stored.Title, stored.Symbol, passed, total, def.Direction, snapshot.Price);
            await strategyRepo.RecordFiredAsync(stored.Id, ct);
        }
        else
        {
            logger.LogInformation("[{Title}] {Symbol} {Passed}/{Total} — waiting on: {Verb}",
                stored.Title, stored.Symbol, passed, total, firstFailure ?? "(unknown)");
        }
    }

    /// <summary>
    /// Collect EMA periods referenced by any condition (entry or branched) so
    /// the snapshot builder pre-computes them. Mirrors DslStrategy's logic;
    /// kept here so the Monitor doesn't need to reach into the adapter's privates.
    /// </summary>
    private static IEnumerable<int> CollectEmaPeriods(StrategyDefinition def)
    {
        var periods = new HashSet<int>();
        foreach (var c in def.EntryConditions)
            CollectFrom(c, periods);
        foreach (var block in def.ConditionalBlocks)
            foreach (var branch in block.Branches)
            {
                if (branch.Condition is not null) CollectFrom(branch.Condition, periods);
                foreach (var c in branch.Overrides.EntryConditions) CollectFrom(c, periods);
            }
        return periods;
    }

    private static void CollectFrom(ICondition c, HashSet<int> bucket)
    {
        if (c is IndicatorCondition ic)
        {
            if (ic.Type is IndicatorType.EmaAbove or IndicatorType.EmaBelow or IndicatorType.ReclaimEma
                && ic.Parameter is { } p)
                bucket.Add((int)p);
            else if (ic.Type is IndicatorType.BetweenEma or IndicatorType.EmaStack
                     && ic.Parameter is { } p1 && ic.Parameter2 is { } p2)
            {
                bucket.Add((int)p1);
                bucket.Add((int)p2);
            }
        }
        else if (c is AndCondition a) { CollectFrom(a.Left, bucket); CollectFrom(a.Right, bucket); }
        else if (c is OrCondition  o) { CollectFrom(o.Left, bucket); CollectFrom(o.Right, bucket); }
        else if (c is NotCondition n) { CollectFrom(n.Inner, bucket); }
    }

    /// <summary>Lenient interval parser — accepts "30s", "5m", "120" (seconds), or null.</summary>
    private static TimeSpan? TryParseInterval(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        raw = raw.Trim().ToLowerInvariant();
        if (raw.EndsWith("ms") && double.TryParse(raw[..^2], out var ms)) return TimeSpan.FromMilliseconds(ms);
        if (raw.EndsWith('s')  && double.TryParse(raw[..^1], out var s))  return TimeSpan.FromSeconds(s);
        if (raw.EndsWith('m')  && double.TryParse(raw[..^1], out var m))  return TimeSpan.FromMinutes(m);
        if (raw.EndsWith('h')  && double.TryParse(raw[..^1], out var h))  return TimeSpan.FromHours(h);
        if (double.TryParse(raw, out var n)) return TimeSpan.FromSeconds(n);
        return null;
    }
}
