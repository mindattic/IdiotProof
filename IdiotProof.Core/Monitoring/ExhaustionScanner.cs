using System.Text.Json;
using IdiotProof.Core.Data;
using IdiotProof.Core.Services;
using IdiotProof.DataFeeds;
using IdiotProof.Evidence.Adaptation;
using IdiotProof.Models;
using IdiotProof.Scripting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace IdiotProof.Core.Monitoring;

/// <summary>
/// Intraday exhaustion templates (IP-US-S2). For each symbol on the owner's exhaustion list, once the
/// session has printed enough to judge (InformationGate), <see cref="ExhaustionEvaluator"/> decides
/// whether the last closed bar is a capitulation low worth a mean-reversion LONG or a failed new high
/// worth a fade SHORT, using only that symbol's own prior sessions. A cleared long is armed as a
/// PAPER strategy (entry on a break of the low bar's high, guarded by NotFallingKnife, exits derived
/// per ticker); a cleared short is recorded for analysis only, because the paper account cannot short
/// yet (IP-US-K16). Every verdict past the gate is persisted to ExhaustionDecisions.
///
/// Off unless SettingsKv <c>exhaustion.enabled</c> is "true"; refuses to arm anything if the owner's
/// paper route resolves to a live account. Arming only queues the strategy: the Monitor's three gates
/// still guard every order.
/// </summary>
public sealed class ExhaustionScanner(
    StrategyRepository strategyRepo,
    IMarketDataFeed feed,
    UserBrokerResolver brokerResolver,
    SettingsRepository settings,
    AuditLogRepository auditLog,
    IDbContextFactory<AppDbContext> dbFactory,
    ILogger<ExhaustionScanner> logger)
{
    public const string EnabledKey = "exhaustion.enabled";
    public const string OwnerKey = "exhaustion.ownerUserId";
    public const string SymbolsKey = "exhaustion.symbols";
    public const string HistoryDaysKey = "exhaustion.historyDays";
    public const string Author = "IdiotProof Exhaustion";
    public const string ShortAnalysisOnly = "fade short recorded for analysis only: the paper account cannot short yet (IP-US-K16)";

    public sealed record ScanSummary(int Symbols, int Decided, int Armed, int AnalysisOnly, string Note);

    public async Task<bool> IsEnabledAsync(CancellationToken ct = default) => await settings.GetBoolAsync(EnabledKey, ct) ?? false;

    public async Task<ScanSummary> RunScanAsync(DateTime nowUtc, ExhaustionOptions? options = null, CancellationToken ct = default)
    {
        options ??= new ExhaustionOptions();
        if (!await IsEnabledAsync(ct)) return new(0, 0, 0, 0, "disabled");
        if (!Guid.TryParse(await settings.GetAsync(OwnerKey, ct), out var owner)) return new(0, 0, 0, 0, "no owner configured");

        var symbols = ((await settings.GetAsync(SymbolsKey, ct)) ?? "")
            .Split(new[] { ',', ' ', ';' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.ToUpperInvariant()).Distinct().ToList();
        if (symbols.Count == 0) return new(0, 0, 0, 0, "no symbols configured");

        var nowEt = EasternTime.FromUtc(nowUtc);
        if (nowEt.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) return new(symbols.Count, 0, 0, 0, "weekend");
        var openEt = nowEt.Date.AddHours(9).AddMinutes(30);
        if (nowEt < openEt || nowEt >= nowEt.Date.AddHours(16)) return new(symbols.Count, 0, 0, 0, "outside the regular session");
        var openUtc = nowUtc - (nowEt - openEt);

        var broker = await brokerResolver.ResolveAsync(owner, "Paper", ct);
        if (!broker.IsPaper)
        {
            var msg = $"paper route resolves to {broker.BrokerType} LIVE — refusing to arm exhaustion templates";
            logger.LogError("Exhaustion: {Msg}.", msg);
            await auditLog.LogAsync("exhaustion-blocked", $"Exhaustion scan aborted: {msg}.", userId: owner, ct: ct);
            return new(symbols.Count, 0, 0, 0, msg);
        }

        int historyDays = Math.Clamp(await settings.GetIntAsync(HistoryDaysKey, ct) ?? 20, 1, 120);
        var etDate = nowEt.ToString("yyyy-MM-dd");
        int decided = 0, armed = 0, analysisOnly = 0;

        foreach (var symbol in symbols)
        {
            try
            {
                if (await AlreadyArmedTodayAsync(symbol, etDate, ct)) continue;
                var (history, today) = await LoadAsync(symbol, openUtc, nowUtc, historyDays, ct);
                var d = ExhaustionEvaluator.Evaluate(history, today, openUtc, nowUtc, options);
                if (!d.Gate.Ready) continue;

                var row = ToRow(owner, symbol, etDate, nowUtc, d);
                if (d.Arm && d.Side == ExhaustionSide.Short)
                {
                    row.AnalysisOnly = true;
                    row.Reason = $"{d.Reason}; {ShortAnalysisOnly}";
                    analysisOnly++;
                }
                else if (d.Arm && d.Side == ExhaustionSide.Long)
                {
                    if (await strategyRepo.CountActiveForSymbolAsync(owner, symbol, ct) > 0)
                    {
                        row.Reason = "duplicate-active: an armed strategy already trades this symbol";
                    }
                    else
                    {
                        var id = await ArmLongAsync(owner, symbol, d, options, ct);
                        if (id is { } created) { row.Armed = true; row.StrategyId = created; armed++; }
                        else row.Reason = "activate refused";
                    }
                }

                await using (var db = await dbFactory.CreateDbContextAsync(ct))
                {
                    db.ExhaustionDecisions.Add(row);
                    await db.SaveChangesAsync(ct);
                }
                decided++;
                logger.LogInformation("Exhaustion {Symbol}: {Side} {Outcome} — {Reason}", symbol, d.Side,
                    row.Armed ? "ARMED" : row.AnalysisOnly ? "analysis-only" : "skip", row.Reason);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Exhaustion: {Symbol} failed — continuing with the rest.", symbol);
            }
        }

        if (decided > 0)
            await auditLog.LogAsync("exhaustion-scan",
                $"Exhaustion scan: {decided} verdicts, {armed} paper longs armed, {analysisOnly} fade shorts recorded for analysis.",
                userId: owner, ct: ct);
        return new(symbols.Count, decided, armed, analysisOnly, "complete");
    }

    /// <summary>The paper strategy a cleared long becomes: enter on a break of the low bar's high, never into a learned knife.</summary>
    public static StrategyBuilder LongTemplate(string symbol, ExhaustionDecision d, ExhaustionOptions o)
    {
        var p = d.Parameters ?? throw new ArgumentException("A cleared long carries derived parameters.", nameof(d));
        var b = Stock.Ticker(symbol)
            .Name($"{symbol} Exhaustion Long @ {d.ExtremePrice:0.####}")
            .Session(TradingSession.RTH)
            .BreaksAbove(Math.Round(d.TriggerPrice, 4))
            .NotFallingKnife(o.KnifeMaxProbability, o.KnifeMinSamples)
            .Long()
            .Quantity(1)
            .StopLossPercent(p.StopLossPercent)
            .TakeProfitPercent(p.TakeProfitPercent);
        if (p.TrailingStopPercent > 0) b.TrailingStopLoss(p.TrailingStopPercent);
        return b.SellBy("15:55");
    }

    private async Task<Guid?> ArmLongAsync(Guid owner, string symbol, ExhaustionDecision d, ExhaustionOptions o, CancellationToken ct)
    {
        var builder = LongTemplate(symbol, d, o);
        var created = await strategyRepo.CreateAsync(owner,
            title: $"{symbol} Exhaustion Long",
            symbol: symbol,
            scriptText: builder.ToScript(),
            description: $"Auto-armed exhaustion long: {d.Reason} (bounce {d.Reversal?.Probability:P0}, n={d.Reversal?.Samples}).",
            scriptJson: StrategyJson.Serialize(builder.Build()),
            author: Author,
            ct: ct);
        var mutation = await strategyRepo.SetActiveAsync(created.Id, true, owner, ct);
        return mutation == StrategyMutation.Ok ? created.Id : null;
    }

    private async Task<bool> AlreadyArmedTodayAsync(string symbol, string etDate, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.ExhaustionDecisions.AnyAsync(x => x.Symbol == symbol && x.EtDate == etDate && x.Armed, ct);
    }

    /// <summary>Prior regular sessions (one list per ET day, before today) and today's bars from the open.</summary>
    private async Task<(List<IReadOnlyList<Candle>> History, List<Candle> Today)> LoadAsync(string symbol, DateTime openUtc,
        DateTime nowUtc, int historyDays, CancellationToken ct)
    {
        var history = new List<Candle>();
        await foreach (var c in feed.GetHistoricalCandlesAsync(symbol, openUtc.AddDays(-Math.Ceiling(historyDays * 1.6) - 3), openUtc, TimeSpan.FromMinutes(1), ct))
            history.Add(c);
        var sessions = history
            .Where(c => IsRegularSession(c.StartUtc))
            .GroupBy(c => EasternTime.FromUtc(c.StartUtc).Date)
            .OrderBy(g => g.Key)
            .TakeLast(historyDays)
            .Select(g => (IReadOnlyList<Candle>)g.OrderBy(c => c.StartUtc).ToList())
            .ToList();

        var today = new List<Candle>();
        await foreach (var c in feed.GetHistoricalCandlesAsync(symbol, openUtc, nowUtc, TimeSpan.FromMinutes(1), ct))
            today.Add(c);
        return (sessions, today);
    }

    private static bool IsRegularSession(DateTime utc)
    {
        var t = EasternTime.FromUtc(utc).TimeOfDay;
        return t >= new TimeSpan(9, 30, 0) && t < new TimeSpan(16, 0, 0);
    }

    private static ExhaustionDecisionRow ToRow(Guid owner, string symbol, string etDate, DateTime nowUtc, ExhaustionDecision d) => new()
    {
        OwnerUserId = owner, Symbol = symbol, EtDate = etDate, DecidedUtc = nowUtc,
        Side = d.Side.ToString(), Reason = Trim(d.Reason), ClosedBars = d.ClosedBars,
        MoveAtr = d.MoveAtr, ExtremePrice = d.ExtremePrice, TriggerPrice = d.TriggerPrice,
        ReversalSuccesses = d.Reversal?.Successes, ReversalSamples = d.Reversal?.Samples,
        ReversalProbability = d.Reversal?.Probability, ReversalLow = d.Reversal?.Low, ReversalHigh = d.Reversal?.High,
        TrapDetector = d.Trap?.Detector, TrapProbability = d.Trap?.Rate.Probability, TrapSamples = d.Trap?.Rate.Samples,
        StopLossPercent = d.Parameters?.StopLossPercent, TrailingStopPercent = d.Parameters?.TrailingStopPercent,
        TakeProfitPercent = d.Parameters?.TakeProfitPercent, AtrPercent = d.Parameters?.AtrPercent,
        Regime = d.Parameters?.Regime.Regime.ToString(),
        FeaturesJson = JsonSerializer.Serialize(new { bucket = d.Trap?.Bucket, features = d.Trap?.Features?.Select(Finite) }),
    };

    private static double? Finite(double v) => double.IsFinite(v) ? v : null;
    private static string Trim(string s) => s.Length <= 400 ? s : s[..400];
}
