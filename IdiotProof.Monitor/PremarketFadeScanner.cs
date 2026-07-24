using IdiotProof.Blazor.Services;
using IdiotProof.DataFeeds;
using IdiotProof.Models;
using IdiotProof.Scripting;
using Microsoft.Extensions.Logging;

namespace IdiotProof.Monitor;

/// <summary>
/// Detects a premarket "blow-off / fade" pattern: a stock surges hard off its
/// premarket low, then rolls over before the 9:30 ET bell — a chart that looks
/// clean in hindsight (OHMH: ~$0.40 → ~$2.20 → faded to ~$1.20 by the open) but
/// baits a long-breakout entry right into the top.
///
/// Detection-only — no strategy is created, no order is placed. Mirrors
/// <see cref="AutoGapperScanner"/>'s 2-stage shape (cheap movers screen → a
/// detailed per-candidate premarket-bar walk via <see cref="StrategyScanner
/// .FetchMoversAsync"/> and <see cref="IMarketDataFeed.GetHistoricalCandlesAsync"/>)
/// but on a different clock (9:00-9:05 AM ET, not 3:55 AM) and for a different
/// purpose (alert the user before the bell, not arm a paper strategy).
/// </summary>
public sealed class PremarketFadeScanner(
    IMarketDataFeed feed,
    UserKeyService userKeys,
    AuditLogRepository auditLogRepo,
    EmailSmsAlertSender alertSender,
    ILogger<PremarketFadeScanner> logger)
{
    /// <summary>Surge off the premarket low that always fires the alert.</summary>
    private const double SurgeThresholdPercent = 20.0;

    /// <summary>
    /// Additional giveback off the peak (as a percent of the low→peak run,
    /// matching GapperExitEvaluator's own PeakGiveback convention) that
    /// escalates the alert wording to "ESPECIALLY".
    /// </summary>
    private const double GivebackThresholdPercent = 10.0;

    public sealed record FadeCandidate(
        string Symbol, double PremarketLow, double PeakPrice, double CurrentPrice,
        double SurgePercent, double GivebackPercent, bool Escalated);

    public sealed record ScanSummary(int Screened, int Flagged, string Note);

    // Per-symbol dedup so a name flagged at 9:00 doesn't re-alert every 5
    // minutes through 10:00 — but an upgrade from "just surging" to "surging
    // AND fading" (the ESPECIALLY case) still gets its own alert once, since
    // that's a materially more urgent message. Keyed by ET calendar date so
    // it naturally resets the next trading day; this is instance state on a
    // singleton that lives for the process's lifetime, matching how the rest
    // of the Monitor tracks "already handled today" (see MonitorWorker's
    // lastPruneUtc/lastFadeScanUtc) — a restart mid-window just means a
    // handful of names might re-alert once, not a correctness problem.
    private readonly Dictionary<string, (DateOnly Day, bool Escalated)> alerted = new(StringComparer.OrdinalIgnoreCase);

    public async Task<ScanSummary> RunScanAsync(Guid userId, CancellationToken ct)
    {
        var k = await userKeys.GetOrCreateAsync(userId, ct);
        if (string.IsNullOrWhiteSpace(k.AlpacaApiKeyId) || string.IsNullOrWhiteSpace(k.AlpacaApiSecretKey))
            return new ScanSummary(0, 0, "user has no Alpaca data keys");

        List<(string Symbol, double Percent, double Price)> movers;
        try { movers = await StrategyScanner.FetchMoversAsync(k.AlpacaApiKeyId!, k.AlpacaApiSecretKey!, 50); }
        catch (Exception ex) { return new ScanSummary(0, 0, $"movers screen failed: {ex.Message}"); }

        var nowUtc = DateTime.UtcNow;
        var todayEt = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(nowUtc, MarketTime.Eastern));
        var flagged = 0;

        // One bad ticker (glitched bar, feed hiccup) must not abort the scan
        // for every other candidate — same lesson as AutoGapperScanner's
        // uncaught-exception fix.
        foreach (var m in movers.GroupBy(m => m.Symbol, StringComparer.OrdinalIgnoreCase).Select(g => g.First()))
        {
            var sym = m.Symbol.ToUpperInvariant();
            try
            {
                var candidate = await AnalyzeAsync(sym, nowUtc, ct);
                if (candidate is null) continue;

                if (alerted.TryGetValue(sym, out var prior) && prior.Day == todayEt
                    && (prior.Escalated || !candidate.Escalated))
                    continue; // already alerted today at the same or higher severity

                await AlertAsync(candidate, userId, ct);
                alerted[sym] = (todayEt, candidate.Escalated);
                flagged++;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "PremarketFadeScanner: analysis failed for {Symbol} — skipping.", sym);
            }
        }

        return new ScanSummary(movers.Count, flagged, "complete");
    }

    private async Task<FadeCandidate?> AnalyzeAsync(string symbol, DateTime nowUtc, CancellationToken ct)
    {
        var etMidnightUtc = TimeZoneInfo.ConvertTimeToUtc(
            DateTime.SpecifyKind(DateTime.Parse(EtDate(nowUtc)).Date.Add(new TimeSpan(4, 0, 0)), DateTimeKind.Unspecified),
            MarketTime.Eastern);

        var bars = new List<Candle>();
        await foreach (var c in feed.GetHistoricalCandlesAsync(symbol, etMidnightUtc, nowUtc, TimeSpan.FromMinutes(1), ct))
            bars.Add(c);
        return ComputeFade(symbol, bars);
    }

    /// <summary>
    /// The pure surge/giveback computation — no network/feed dependency, so
    /// it's directly unit-testable against a synthetic bar series.
    /// </summary>
    public static FadeCandidate? ComputeFade(string symbol, IReadOnlyList<Candle> bars)
    {
        if (bars.Count < 2) return null;

        var low = (double)bars.Min(c => c.Low);
        var peak = (double)bars.Max(c => c.High);
        var current = (double)bars[^1].Close;
        if (low <= 0 || peak <= low) return null;

        var run = peak - low;
        var surgePercent = run / low * 100.0;
        if (surgePercent < SurgeThresholdPercent) return null;

        var givebackPercent = peak > current ? (peak - current) / run * 100.0 : 0.0;
        var escalated = givebackPercent >= GivebackThresholdPercent;

        return new FadeCandidate(symbol, low, peak, current, surgePercent, givebackPercent, escalated);
    }

    private async Task AlertAsync(FadeCandidate c, Guid userId, CancellationToken ct)
    {
        var headline = c.Escalated
            ? $"ESPECIALLY {c.Symbol}: surged +{c.SurgePercent:0.#}% premarket (${c.PremarketLow:F2}→${c.PeakPrice:F2}) then gave back {c.GivebackPercent:0.#}% off the peak before the bell — now ${c.CurrentPrice:F2}"
            : $"{c.Symbol}: surged +{c.SurgePercent:0.#}% premarket (${c.PremarketLow:F2}→${c.PeakPrice:F2}), now ${c.CurrentPrice:F2}";

        logger.LogWarning("PremarketFadeScanner: {Headline}", headline);
        await auditLogRepo.LogAsync("premarket-fade-alert", headline, userId: userId, ct: ct);

        var sent = await alertSender.TrySendAsync($"IdiotProof: {headline}", ct);
        if (!sent)
            logger.LogWarning("PremarketFadeScanner: phone alert for {Symbol} was not sent (see prior warning for the reason).", c.Symbol);
    }

    private static string EtDate(DateTime utc) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), MarketTime.Eastern))
            .ToString("yyyy-MM-dd");
}
