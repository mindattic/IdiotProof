// ============================================================================
// GapperSequenceTemplates - long-only multi-step gapper strategies (IP-T527)
// ============================================================================
//
// Sequenced ("Steps") versions of the flagship gapper trade, written in the DSL
// and registered in gapper study amendment 07 before anything was computed.
// Step 0 of every template is the classic-gapper ride with the catalog's own
// numbers, so a sequence differs from the single-step gapper only by what it
// does after the ride is flat.
//
// They are SAMPLES: `strategy samples` shows them and `backtest run` replays
// them. None is a gapper profile and nothing arms them; the study's verdict is
// docs/research/gapper-study-2026-11/gapper-sequences.md.
// ============================================================================

using IdiotProof.Models;

namespace IdiotProof.Scripting;

/// <summary>
/// One multi-step gapper template: its id, what it does, whether amendment 07 evaluates it on the
/// study's train days, and the builder for a ticker (1 share per step, long only).
/// </summary>
public sealed record GapperSequenceTemplate(string Id, string Pattern, string Teaches, bool Evaluated, Func<string, StrategyBuilder> Compose);

public static class GapperSequenceTemplates
{
    public const string RideThenVwapPullback = "gapper-ride-vwap-pullback";
    public const string RideThenRetest = "gapper-ride-retest";
    public const string RideThenDayTwo = "gapper-ride-day-two";

    public static readonly IReadOnlyList<GapperSequenceTemplate> All =
    [
        new(RideThenVwapPullback, "Gapper ride, flat into the bell, then the first higher low above VWAP after the open",
            "Steps: a premarket ride, ThenWhen + Within as the hand-off, and a second long leg with its own window, stop and sell-by",
            Evaluated: true,
            symbol => Ride(symbol, "ride, then VWAP pullback")
                .ThenWhen(c => c.IsAboveVwap()).Within(1)
                .Step("pullback").Quantity(1)
                .RequireEntryWindow("09:35", "10:30").IsAboveVwap().IsHigherLow().IsGapUp(5)
                .Long().StopLossPercent(8).PeakGiveback(25).SellBy("11:00")),
        new(RideThenRetest, "Gapper ride, flat into the bell, then a second leg only if the open retests the pre-bell high with speed",
            "Steps: the second leg is conditional on strength after the open (near the 20-bar high, above VWAP, rising) and is out by 09:44",
            Evaluated: true,
            symbol => Ride(symbol, "ride, then pre-bell high retest")
                .ThenWhen(c => c.IsAboveVwap()).Within(1)
                .Step("retest").Quantity(1)
                .RequireEntryWindow("09:31", "09:40").IsAboveVwap().IsAtResistance(1).VelocityAbove(0.2, 3).IsGapUp(5)
                .Long().StopLossPercent(8).PeakGiveback(25).SellBy("09:44")),
        new(RideThenDayTwo, "Gapper ride on day one, then a second premarket ride within one day",
            "Steps across days: Within(1) is the deadline for the hand-off; replay it with `backtest run --days 2`",
            Evaluated: false,
            symbol => Ride(symbol, "ride, then day two")
                .ThenWhen(c => c.IsAboveVwap().And(c.IsHigherLow())).Within(1)
                .Step("day-two").Quantity(1)
                .RequireEntryWindow("04:00", "09:00").IsGapUp(5).IsVolumeAbove(2).IsPriceBetween(1, 50)
                .Long().StopLossPercent(5).PeakGiveback(25, "09:15").SellBy("09:28")),
    ];

    public static GapperSequenceTemplate? Find(string id) =>
        All.FirstOrDefault(t => t.Id.Equals((id ?? "").Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Step 0: the classic-gapper profile's ride (gapper-profiles.json: 5%+ gap on 2x volume, $1-$50, in between
    /// 04:00 and 09:00, 5% stop, 25% peak giveback from 09:15, flat by 09:28). The session is Extended because a
    /// later step trades after the bell.
    /// </summary>
    private static StrategyBuilder Ride(string symbol, string what) =>
        Stock.Ticker(symbol)
            .Name($"{symbol.ToUpperInvariant()} Gapper sequence - {what}")
            .Session(TradingSession.Extended)
            .Quantity(1)
            .Step("ride")
            .RequireEntryWindow("04:00", "09:00").IsGapUp(5).IsVolumeAbove(2).IsPriceBetween(1, 50)
            .Long().StopLossPercent(5).PeakGiveback(25, "09:15").SellBy("09:28");
}
